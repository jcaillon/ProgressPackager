#region header

// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (ProExecution.cs) is part of csdeployer.
// 
// csdeployer is a free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// csdeployer is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with csdeployer. If not, see <http://www.gnu.org/licenses/>.
// ========================================================================

#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using abldeployer.Core.Exceptions;
using abldeployer.Lib;

namespace abldeployer.Core {
    #region ProExecution

    /// <summary>
    ///     Base class for all the progress execution (i.e. when we need to start a prowin process and do something)
    /// </summary>
    public abstract class ProExecution {
        #region Do

        /// <summary>
        ///     allows to prepare the execution environment by creating a unique temp folder
        ///     and copying every critical files into it
        ///     Then execute the progress program
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ExecutionException"></exception>
        public void Start() {
            // check parameters
            CheckParameters();

            // create a unique temporary folder
            _localTempDir = Path.Combine(ProEnv.FolderTemp, "exec_" + DateTime.Now.ToString("HHmmssfff") + "_" + Path.GetRandomFileName());
            if (!Directory.Exists(_localTempDir)) Directory.CreateDirectory(_localTempDir);

            // move .ini file into the execution directory
            if (File.Exists(ProEnv.IniPath)) {
                _tempInifilePath = Path.Combine(_localTempDir, "base.ini");

                // we need to copy the .ini but we must delete the PROPATH= part, as stupid as it sounds, if we leave a huge PROPATH 
                // in this file, it increases the compilation time by a stupid amount... unbelievable i know, but trust me, it does...
                var encoding = TextEncodingDetect.GetFileEncoding(ProEnv.IniPath);
                var fileContent = Utils.ReadAllText(ProEnv.IniPath, encoding);
                var regex = new Regex("^PROPATH=.*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                var matches = regex.Match(fileContent);
                if (matches.Success)
                    fileContent = regex.Replace(fileContent, @"PROPATH=");
                File.WriteAllText(_tempInifilePath, fileContent, encoding);
            }

            // set common info on the execution
            _processStartDir = _localTempDir;
            _logPath = Path.Combine(_localTempDir, "run.log");
            _dbLogPath = Path.Combine(_localTempDir, "db.ko");
            _notifPath = Path.Combine(_localTempDir, "postExecution.notif");
            _propath = (_localTempDir + "," + string.Join(",", ProEnv.GetProPathDirList.ToArray())).Trim().Trim(',');
            _propathFilePath = Path.Combine(_localTempDir, "progress.propath");
            File.WriteAllText(_propathFilePath, _propath, Encoding.Default);

            // Set info
            SetExecutionInfo();
            SetPreprocessedVar("ExecutionType", ExecutionType.ToString().ToUpper().PreProcQuoter());
            SetPreprocessedVar("LogPath", _logPath.PreProcQuoter());
            SetPreprocessedVar("PropathFilePath", _propathFilePath.PreProcQuoter());
            SetPreprocessedVar("DbConnectString", ProEnv.ConnectionString.PreProcQuoter());
            SetPreprocessedVar("DbLogPath", _dbLogPath.PreProcQuoter());
            SetPreprocessedVar("DbConnectionMandatory", NeedDatabaseConnection.ToString());
            SetPreprocessedVar("NotificationOutputPath", _notifPath.PreProcQuoter());
            SetPreprocessedVar("PreExecutionProgram", ProEnv.PreExecutionProgram.Trim().PreProcQuoter());
            SetPreprocessedVar("PostExecutionProgram", ProEnv.PostExecutionProgram.Trim().PreProcQuoter());

            // prepare the .p runner
            _runnerPath = Path.Combine(_localTempDir, "run_" + DateTime.Now.ToString("HHmmssfff") + ".p");
            var runnerProgram = new StringBuilder();
            foreach (var var in _preprocessedVars) runnerProgram.AppendLine("&SCOPED-DEFINE " + var.Key + " " + var.Value);
            runnerProgram.Append(Encoding.Default.GetString(ProEnv.ProgramProgressRun));
            File.WriteAllText(_runnerPath, runnerProgram.ToString(), Encoding.Default);

            // no batch mode option?
            _useBatchMode = !ProEnv.NeverUseProwinInBatchMode && !NoBatch && CanUseBatchMode();

            // Parameters
            _exeParameters = new StringBuilder();
            if (_useBatchMode) {
                _exeParameters.Append(" -b");
            } else {
                // we suppress the splashscreen
                if (ProEnv.CanProwinUseNoSplash) _exeParameters.Append(" -nosplash");
                else MoveSplashScreenNoError(Path.Combine(Path.GetDirectoryName(ProEnv.ProwinPath) ?? "", "splashscreen.bmp"), Path.Combine(Path.GetDirectoryName(ProEnv.ProwinPath) ?? "", "splashscreen-3p-disabled.bmp"));
            }
            _exeParameters.Append(" -p " + _runnerPath.Quoter());
            if (!string.IsNullOrWhiteSpace(ProEnv.CmdLineParameters))
                _exeParameters.Append(" " + ProEnv.CmdLineParameters.Trim());
            AppendProgressParameters(_exeParameters);

            // start the process
            StartProcess();
        }

        #endregion

        #region Events

        /// <summary>
        ///     The action to execute just after the end of a prowin process
        /// </summary>
        public event Action<ProExecution> OnExecutionEnd;

        /// <summary>
        ///     The action to execute at the end of the process if it went well = we found a .log and the database is connected or
        ///     is not mandatory
        /// </summary>
        public event Action<ProExecution> OnExecutionOk;

        /// <summary>
        ///     The action to execute at the end of the process if something went wrong (no .log or database down)
        /// </summary>
        public event Action<ProExecution> OnExecutionFailed;

        #endregion

        #region Options

        /// <summary>
        ///     set to true if a valid database connection is mandatory (the compilation will not be done if a db can't be
        ///     connected
        /// </summary>
        public bool NeedDatabaseConnection { get; set; }

        /// <summary>
        ///     Set to true to not use the batch mode
        /// </summary>
        public bool NoBatch { get; set; }

        #endregion

        #region Properties

        /// <summary>
        ///     Copy of the pro env to use
        /// </summary>
        public Config.ProConfig ProEnv { get; private set; }

        /// <summary>
        ///     set to true if a the execution process has been killed
        /// </summary>
        public bool HasBeenKilled { get; private set; }

        /// <summary>
        ///     Set to true after the process is over if the execution failed
        /// </summary>
        public bool ExecutionFailed { get; private set; }

        /// <summary>
        ///     Set to true after the process is over if the database connection has failed
        /// </summary>
        public bool ConnectionFailed { get; private set; }

        /// <summary>
        ///     Execution type of the current class
        /// </summary>
        public virtual ExecutionType ExecutionType {
            get { return ExecutionType.Compile; }
        }

        public List<ExecutionException> HandledExceptions { get; private set; }

        #endregion

        #region Private fields

        /// <summary>
        ///     Full file path to the output file for the custom post-execution notification
        /// </summary>
        protected string _notifPath;

        protected string _tempInifilePath;

        protected Dictionary<string, string> _preprocessedVars;

        /// <summary>
        ///     Path to the output .log file (for compilation)
        /// </summary>
        protected string _logPath;

        /// <summary>
        ///     log to the database connection log (not existing if everything is ok)
        /// </summary>
        protected string _dbLogPath;

        /// <summary>
        ///     Full path to the directory containing all the files needed for the execution
        /// </summary>
        protected string _localTempDir;

        /// <summary>
        ///     Full path to the directory used as the working directory to start the prowin process
        /// </summary>
        protected string _processStartDir;

        protected string _propath;

        protected string _propathFilePath;

        /// <summary>
        ///     Parameters of the .exe call
        /// </summary>
        protected StringBuilder _exeParameters;

        protected Process _process;

        protected bool _useBatchMode;

        protected string _runnerPath;

        #endregion

        #region Life and death

        /// <summary>
        ///     Deletes temp directory and everything in it
        /// </summary>
        ~ProExecution() {
            Clean();
        }

        public ProExecution(Config.ProConfig proEnv) {
            ProEnv = proEnv;
            _preprocessedVars = new Dictionary<string, string> {
                {"LogPath", "\"\""},
                {"DbLogPath", "\"\""},
                {"PropathFilePath", "\"\""},
                {"DbConnectString", "\"\""},
                {"ExecutionType", "\"\""},
                {"CurrentFilePath", "\"\""},
                {"OutputPath", "\"\""},
                {"ToCompileListFile", "\"\""},
                {"AnalysisMode", "false"},
                {"CompileProgressionFile", "\"\""},
                {"DbConnectionMandatory", "false"},
                {"NotificationOutputPath", "\"\""},
                {"PreExecutionProgram", "\"\""},
                {"PostExecutionProgram", "\"\""}
            };
        }

        #endregion

        #region public methods

        /// <summary>
        ///     Allows to kill the process of this execution (be careful, the OnExecutionEnd, Ok, Fail events are not executed in
        ///     that case!)
        /// </summary>
        public void KillProcess() {
            try {
                _process.Kill();
                _process.Close();
            } catch (Exception) {
                // ignored
            }
            HasBeenKilled = true;
        }

        public void WaitForProcessExit(int maxWait = 3000) {
            if (maxWait > 0)
                _process.WaitForExit(maxWait);
            else _process.WaitForExit();
        }

        public bool DbConnectionFailedOnMaxUser {
            get { return (Utils.ReadAllText(_dbLogPath, Encoding.Default) ?? "").Contains("(748)"); }
        }

        #endregion

        #region To override

        /// <summary>
        ///     Should return null or the message error that indicates which parameter is incorrect
        /// </summary>
        /// <exception cref="ExecutionException"></exception>
        protected virtual void CheckParameters() {
            // check prowin32.exe
            if (!File.Exists(ProEnv.ProwinPath)) throw new ExecutionParametersException("Couldn't start an execution, the following file does not exist : " + ProEnv.ProwinPath.Quoter());
        }

        /// <summary>
        ///     Return true if can use batch mode
        /// </summary>
        protected virtual bool CanUseBatchMode() {
            return false;
        }

        /// <summary>
        ///     Extra stuff to do before executing
        /// </summary>
        /// <exception cref="ExecutionException"></exception>
        protected virtual void SetExecutionInfo() { }

        /// <summary>
        ///     Add stuff to the command line
        /// </summary>
        protected virtual void AppendProgressParameters(StringBuilder sb) {
            if (!string.IsNullOrEmpty(_tempInifilePath))
                sb.Append(" -ininame " + _tempInifilePath.Quoter() + " -basekey " + "INI".Quoter());
        }

        #endregion

        #region private methods

        /// <summary>
        ///     Allows to clean the temporary directories
        /// </summary>
        public virtual void Clean() {
            try {
                if (_process != null)
                    _process.Close();

                // delete temp dir
                if (_localTempDir != null)
                    Directory.Delete(_localTempDir, true);

                // restore splashscreen
                if (!string.IsNullOrEmpty(ProEnv.ProwinPath))
                    MoveSplashScreenNoError(Path.Combine(Path.GetDirectoryName(ProEnv.ProwinPath) ?? "", "splashscreen-3p-disabled.bmp"), Path.Combine(Path.GetDirectoryName(ProEnv.ProwinPath) ?? "", "splashscreen.bmp"));
            } catch (Exception) {
                // dont care
            }
        }

        /// <summary>
        ///     set pre-processed variable for the runner program
        /// </summary>
        protected void SetPreprocessedVar(string key, string value) {
            if (!_preprocessedVars.ContainsKey(key))
                _preprocessedVars.Add(key, value);
            else
                _preprocessedVars[key] = value;
        }

        /// <summary>
        ///     Start the prowin process with the options defined in this object
        /// </summary>
        protected virtual void StartProcess() {
            var pInfo = new ProcessStartInfo {
                FileName = ProEnv.ProwinPath,
                Arguments = _exeParameters.ToString(),
                WorkingDirectory = _processStartDir
            };
            if (_useBatchMode) {
                pInfo.WindowStyle = ProcessWindowStyle.Hidden;
                pInfo.CreateNoWindow = true;
            }
            _process = new Process {
                StartInfo = pInfo,
                EnableRaisingEvents = true
            };
            _process.Exited += ProcessOnExited;
            _process.Start();
        }

        /// <summary>
        ///     Called by the process's thread when it is over, execute the ProcessOnExited event
        /// </summary>
        private void ProcessOnExited(object sender, EventArgs eventArgs) {
            try {
                // if log not found then something is messed up!
                if (string.IsNullOrEmpty(_logPath) || !File.Exists(_logPath)) {
                    AddHandledExceptions(new ExecutionException("An error has occurred during the execution : " + ProEnv.ProwinPath + " " + _exeParameters + ", in the directory : " + _process.StartInfo.WorkingDirectory));
                    ExecutionFailed = true;
                } else if (new FileInfo(_logPath).Length > 0) {
                    // else if the log isn't empty, something went wrong
                    var logContent = Utils.ReadAllText(_logPath, Encoding.Default).Trim();
                    if (!string.IsNullOrEmpty(logContent)) {
                        AddHandledExceptions(new ExecutionException("An error has occurred during the execution : " + logContent));
                        ExecutionFailed = true;
                    }
                }

                // if the db log file exists, then the connect statement failed, warn the user
                if (File.Exists(_dbLogPath) && new FileInfo(_dbLogPath).Length > 0) {
                    AddHandledExceptions(new ExecutionException("An error has occurred when connecting to the database : " + Utils.ReadAllText(_dbLogPath, Encoding.Default)));
                    ConnectionFailed = true;
                }
            } catch (Exception e) {
                AddHandledExceptions(e);
            } finally {
                PublishExecutionEndEvents();
            }
        }

        /// <summary>
        ///     publish the end of execution events
        /// </summary>
        protected virtual void PublishExecutionEndEvents() {
            // end of successful/unsuccessful execution action
            try {
                if (ExecutionFailed || ConnectionFailed && NeedDatabaseConnection) {
                    if (OnExecutionFailed != null) OnExecutionFailed(this);
                } else {
                    if (OnExecutionOk != null) OnExecutionOk(this);
                }
            } catch (Exception e) {
                AddHandledExceptions(e);
            }

            // end of execution action
            try {
                if (OnExecutionEnd != null) OnExecutionEnd(this);
            } catch (Exception e) {
                AddHandledExceptions(e);
            }
        }

        /// <summary>
        ///     move a file, catch the errors
        /// </summary>
        private void MoveSplashScreenNoError(string from, string to) {
            if (File.Exists(from))
                try {
                    File.Move(from, to);
                } catch (Exception) {
                    // if it fails it is not really a problem
                }
        }

        protected void AddHandledExceptions(Exception exception, string customMessage = null) {
            if (HandledExceptions == null)
                HandledExceptions = new List<ExecutionException>();
            if (customMessage != null)
                HandledExceptions.Add(new ExecutionException(customMessage, exception));
            else HandledExceptions.Add(new ExecutionException("ABL Execution exception", exception));
        }

        #endregion
    }

    #endregion

    #region ProExecutionProVersion

    public class ProExecutionProVersion : ProExecution {
        private string _outputPath;

        public override ExecutionType ExecutionType {
            get { return ExecutionType.ProVersion; }
        }

        public string ProVersion {
            get { return Utils.ReadAllText(_outputPath, Encoding.Default); }
        }

        public ProExecutionProVersion(Config.ProConfig proEnv) : base(proEnv) { }

        protected override void SetExecutionInfo() {
            _outputPath = Path.Combine(_localTempDir, "pro.version");
            SetPreprocessedVar("OutputPath", _outputPath.PreProcQuoter());
        }

        protected override void AppendProgressParameters(StringBuilder sb) {
            sb.Clear();
            _exeParameters.Append(" -b -p " + _runnerPath.Quoter());
        }

        protected override bool CanUseBatchMode() {
            return true;
        }
    }

    #endregion

    #region ProExecutionTableCrc

    /// <summary>
    ///     Allows to output a file containing the structure of the database
    /// </summary>
    public class ProExecutionTableCrc : ProExecution {
        public ProExecutionTableCrc(Config.ProConfig proEnv) : base(proEnv) { }

        #region Methods

        /// <summary>
        ///     Get a list with all the tables + CRC
        /// </summary>
        /// <returns></returns>
        public List<TableCrc> GetTableCrc() {
            var output = new List<TableCrc>();
            Utils.ForEachLine(OutputPath, new byte[0], (i, line) => {
                var split = line.Split('\t');
                if (split.Length == 2)
                    output.Add(new TableCrc {
                        QualifiedTableName = split[0],
                        Crc = split[1]
                    });
            }, Encoding.Default);
            return output;
        }

        #endregion

        #region Properties

        public override ExecutionType ExecutionType {
            get { return ExecutionType.TableCrc; }
        }

        /// <summary>
        ///     File to the output path that contains the CRC of each table
        /// </summary>
        public string OutputPath { get; set; }

        #endregion

        #region Override

        protected override void SetExecutionInfo() {
            OutputPath = Path.Combine(_localTempDir, "db.extract");
            SetPreprocessedVar("OutputPath", OutputPath.PreProcQuoter());

            var fileToExecute = "db_" + DateTime.Now.ToString("yyMMdd_HHmmssfff") + ".p";
            File.WriteAllBytes(Path.Combine(_localTempDir, fileToExecute), ProEnv.ProgramDumpTableCrc);
            SetPreprocessedVar("CurrentFilePath", fileToExecute.PreProcQuoter());
        }

        protected override bool CanUseBatchMode() {
            return true;
        }

        #endregion
    }

    #endregion

    #region ProExecutionDeploymentHook

    public class ProExecutionDeploymentHook : ProExecution {
        public override ExecutionType ExecutionType {
            get { return ExecutionType.DeploymentHook; }
        }

        public string DeploymentSourcePath { get; set; }

        public int DeploymentStep { get; set; }

        public ProExecutionDeploymentHook(Config.ProConfig proEnv) : base(proEnv) { }

        protected override void SetExecutionInfo() {
            var fileToExecute = "hook_" + DateTime.Now.ToString("yyMMdd_HHmmssfff") + ".p";
            var hookProc = new StringBuilder();
            hookProc.AppendLine("&SCOPED-DEFINE StepNumber " + DeploymentStep);
            hookProc.AppendLine("&SCOPED-DEFINE SourceDirectory " + DeploymentSourcePath.PreProcQuoter());
            hookProc.AppendLine("&SCOPED-DEFINE DeploymentDirectory " + ProEnv.TargetDirectory.PreProcQuoter());
            var encoding = TextEncodingDetect.GetFileEncoding(ProEnv.FileDeploymentHook);
            File.WriteAllText(Path.Combine(_localTempDir, fileToExecute), Utils.ReadAllText(ProEnv.FileDeploymentHook, encoding).Replace(@"/*<inserted_3P_values>*/", hookProc.ToString()), encoding);

            SetPreprocessedVar("CurrentFilePath", fileToExecute.PreProcQuoter());
        }
    }

    #endregion

    #region ExecutionType

    public enum ExecutionType {
        Compile = 1,
        DeploymentHook = 17,
        ProVersion = 18,
        TableCrc = 19
    }

    #endregion
}