#region header

// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (ProExecutionHandleCompilation.cs) is part of csdeployer.
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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using abldeployer.Core.Exceptions;
using abldeployer.Lib;

namespace abldeployer.Core {
    #region ProExecutionHandleCompilation

    public abstract class ProExecutionHandleCompilation : ProExecution {
        #region public methods

        /// <summary>
        ///     Number of files already treated
        /// </summary>
        public int NbFilesTreated {
            get { return unchecked((int) (File.Exists(_progressionFilePath) ? new FileInfo(_progressionFilePath).Length : 0)); }
        }

        #endregion

        #region Static events

        /// <summary>
        ///     The action to execute at the end of the compilation if it went well
        ///     - the list of all the files that needed to be compiled,
        ///     - the errors for each file compiled (if any)
        ///     - the list of all the deployments needed for the files compiled (move the .r but also .dbg and so on...)
        /// </summary>
        public static event Action<ProExecutionHandleCompilation, List<FileToCompile>, List<FileToDeploy>> OnEachCompilationOk;

        #endregion

        #region Events

        /// <summary>
        ///     The action to execute at the end of the compilation if it went well. It sends :
        ///     - the list of all the files that needed to be compiled,
        ///     - the errors for each file compiled (if any)
        ///     - the list of all the deployments needed for the files compiled (move the .r but also .dbg and so on...)
        /// </summary>
        public event Action<ProExecutionHandleCompilation, List<FileToCompile>, List<FileToDeploy>> OnCompilationOk;

        #endregion

        #region Options

        /// <summary>
        ///     List of the files to compile / run / prolint
        /// </summary>
        public List<FileToCompile> Files { get; set; }

        /// <summary>
        ///     If true, don't actually do anything, just test it
        /// </summary>
        public bool IsTestMode { get; set; }

        /// <summary>
        ///     Compile with DEBUG-LIST option
        /// </summary>
        public bool CompileWithDebugList { get; set; }

        /// <summary>
        ///     Compile with LISTING option
        /// </summary>
        public bool CompileWithListing { get; set; }

        /// <summary>
        ///     Compile with XREF option
        /// </summary>
        public bool CompileWithXref { get; set; }

        /// <summary>
        ///     Must generate the xref in xml format instead of normal txt
        /// </summary>
        public bool UseXmlXref { get; set; }

        /// <summary>
        ///     When true, we activate the log just before compiling with FileId active + we generate a file that list referenced
        ///     table in the .r
        /// </summary>
        public bool IsAnalysisMode { get; set; }

        #endregion

        #region Properties

        /// <summary>
        ///     Temp directory located in the deployment dir
        /// </summary>
        public string DistantTempDir { get; private set; }

        /// <summary>
        ///     In analysis mode, allows to affect the crc to each used table
        /// </summary>
        public List<TableCrc> TablesCrcs { get; set; }

        #endregion

        #region Constants

        public const string ExtR = ".r";
        public const string ExtDbg = ".dbg";
        public const string ExtLis = ".lis";
        public const string ExtXrf = ".xrf";
        public const string ExtXrfXml = ".xrf.xml";
        public const string ExtCls = ".cls";
        public const string ExtFileIdLog = ".fileidlog";
        public const string ExtRefTables = ".reftables";

        #endregion

        #region private fields

        /// <summary>
        ///     Path to the file containing the COMPILE output
        /// </summary>
        protected string _compilationLog;

        /// <summary>
        ///     Path to a file used to determine the progression of a compilation (useful when compiling multiple programs)
        ///     1 byte = 1 file treated
        /// </summary>
        private string _progressionFilePath;

        #endregion

        #region constructors and destructor

        /// <summary>
        ///     Construct with the current env
        /// </summary>
        public ProExecutionHandleCompilation() : this(null) { }

        public ProExecutionHandleCompilation(Config.ProConfig proEnv) : base(proEnv) {
            // set some options
            CompileWithDebugList = proEnv.CompileWithDebugList;
            CompileWithListing = proEnv.CompileWithListing;
            CompileWithXref = proEnv.CompileWithXref;
            UseXmlXref = proEnv.CompileUseXmlXref;
            IsAnalysisMode = false;

            DistantTempDir = Path.Combine(ProEnv.TargetDirectory, "~3p-tmp-" + DateTime.Now.ToString("HHmmss") + "-" + Path.GetRandomFileName());
        }

        #endregion

        #region Override

        /// <summary>
        ///     Allows to clean the temporary directories
        /// </summary>
        public override void Clean() {
            // delete temp dir
            Utils.DeleteDirectory(DistantTempDir, true);
            base.Clean();
        }

        protected override void SetExecutionInfo() {
            if (Files == null)
                Files = new List<FileToCompile>();

            // for each file of the list
            var filesListcontent = new StringBuilder();
            var count = 1;
            foreach (var fileToCompile in Files) {
                if (!File.Exists(fileToCompile.SourcePath)) throw new ExecutionException("Couldn't find the source file : " + fileToCompile.SourcePath.Quoter());

                var localSubTempDir = Path.Combine(_localTempDir, count.ToString());
                var baseFileName = Path.GetFileNameWithoutExtension(fileToCompile.SourcePath);

                // get the output directory that will be use to generate the .r (and listing debug-list...)
                ComputeOutputDir(fileToCompile, localSubTempDir, count);
                if (!Directory.Exists(fileToCompile.CompilationOutputDir)) Directory.CreateDirectory(fileToCompile.CompilationOutputDir);

                fileToCompile.CompOutputR = Path.Combine(fileToCompile.CompilationOutputDir, baseFileName + ExtR);
                if (CompileWithListing)
                    fileToCompile.CompOutputLis = Path.Combine(fileToCompile.CompilationOutputDir, baseFileName + ExtLis);
                if (CompileWithXref)
                    fileToCompile.CompOutputXrf = Path.Combine(fileToCompile.CompilationOutputDir, baseFileName + (UseXmlXref ? ExtXrfXml : ExtXrf));
                if (CompileWithDebugList)
                    fileToCompile.CompOutputDbg = Path.Combine(fileToCompile.CompilationOutputDir, baseFileName + ExtDbg);

                if (IsAnalysisMode) {
                    if (!Directory.Exists(localSubTempDir)) Directory.CreateDirectory(localSubTempDir);
                    fileToCompile.CompOutputFileIdLog = Path.Combine(localSubTempDir, baseFileName + ExtFileIdLog);
                    fileToCompile.CompOutputRefTables = Path.Combine(localSubTempDir, baseFileName + ExtRefTables);
                }

                fileToCompile.CompiledSourcePath = fileToCompile.SourcePath;

                // feed files list
                filesListcontent.AppendLine(fileToCompile.CompiledSourcePath.Quoter() + " " + fileToCompile.CompilationOutputDir.Quoter() + " " + (fileToCompile.CompOutputLis ?? "?").Quoter() + " " + (fileToCompile.CompOutputXrf ?? "?").Quoter() + " " + (fileToCompile.CompOutputDbg ?? "?").Quoter() + " " + (fileToCompile.CompOutputFileIdLog ?? "").Quoter() + " " + (fileToCompile.CompOutputRefTables ?? "").Quoter());

                count++;
            }
            var filesListPath = Path.Combine(_localTempDir, "files.list");
            File.WriteAllText(filesListPath, filesListcontent.ToString(), Encoding.Default);

            _progressionFilePath = Path.Combine(_localTempDir, "compile.progression");
            _compilationLog = Path.Combine(_localTempDir, "compilation.log");

            SetPreprocessedVar("ToCompileListFile", filesListPath.PreProcQuoter());
            SetPreprocessedVar("CompileProgressionFile", _progressionFilePath.PreProcQuoter());
            SetPreprocessedVar("OutputPath", _compilationLog.PreProcQuoter());
            SetPreprocessedVar("AnalysisMode", IsAnalysisMode.ToString());

            base.SetExecutionInfo();
        }

        /// <summary>
        ///     get the output directory that will be use to generate the .r (and listing debug-list...)
        /// </summary>
        protected virtual void ComputeOutputDir(FileToCompile fileToCompile, string localSubTempDir, int count) {
            fileToCompile.CompilationOutputDir = localSubTempDir;
        }

        /// <summary>
        ///     In test mode, we do as if everything went ok but we don't actually start the process
        /// </summary>
        protected override void StartProcess() {
            if (IsTestMode) PublishExecutionEndEvents();
            else base.StartProcess();
        }

        /// <summary>
        ///     Also publish the end of compilation events
        /// </summary>
        protected override void PublishExecutionEndEvents() {
            // Analysis mode, read output files
            if (IsAnalysisMode)
                try {
                    // do a deployment action for each file
                    Parallel.ForEach(Files, file => { file.ReadAnalysisResults(); });
                } catch (Exception e) {
                    AddHandledExceptions(e, "An error occurred while reading the analysis results");
                }

            // end of successful execution action
            if (!ExecutionFailed && (!ConnectionFailed || !NeedDatabaseConnection))
                try {
                    var errorsList = LoadErrorLog();
                    var deployList = GetFilesToDeployAfterCompilation();

                    // don't try to deploy files with errors...
                    if (deployList != null) foreach (var kpv in errorsList) if (kpv.Value != null && kpv.Value.Exists(error => error.Level >= ErrorLevel.Error)) deployList.RemoveAll(deploy => deploy.Origin.Equals(kpv.Key));

                    foreach (var kpv in errorsList) {
                        var find = Files.Find(file => file.SourcePath.Equals(kpv.Key));
                        if (find != null) find.Errors = kpv.Value;
                    }

                    if (OnCompilationOk != null) OnCompilationOk(this, Files, deployList);

                    if (OnEachCompilationOk != null) OnEachCompilationOk(this, Files, deployList);
                } catch (Exception e) {
                    AddHandledExceptions(e, "An error occurred while reading the compilation results");
                }

            base.PublishExecutionEndEvents();
        }

        #endregion

        #region Private

        /// <summary>
        ///     Creates a list of files to deploy after a compilation,
        ///     for each Origin file will correspond one (or more if it's a .cls) .r file,
        ///     and one .lst if the option has been checked
        /// </summary>
        protected virtual List<FileToDeploy> GetFilesToDeployAfterCompilation() {
            return null;
        }

        /// <summary>
        ///     Read the compilation/prolint errors of a given execution through its .log file
        ///     update the FilesInfo accordingly so the user can see the errors in npp
        /// </summary>
        private Dictionary<string, List<FileError>> LoadErrorLog() {
            // we need to correct the files path in the log if needed
            var changePaths = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var treatedFile in Files.Where(treatedFile => !treatedFile.CompiledSourcePath.Equals(treatedFile.SourcePath)))
                if (!changePaths.ContainsKey(treatedFile.CompiledSourcePath))
                    changePaths.Add(treatedFile.CompiledSourcePath, treatedFile.SourcePath);

            // read the log file
            return GetErrorsList(changePaths);
        }

        protected virtual Dictionary<string, List<FileError>> GetErrorsList(Dictionary<string, string> changePaths) {
            return ReadErrorsFromFile(_compilationLog, false, changePaths);
        }

        /// <summary>
        ///     Reads an error log file, format :
        ///     filepath \t ErrorLevel \t line \t column \t error number \t message \t help
        ///     (column and line can be equals to "?" in that case, they will be forced to 0)
        ///     fromProlint = true allows to set FromProlint to true in the object,
        ///     permutePaths allows to replace a path with another, useful when we compiled from a tempdir but we want the errors
        ///     to appear for the "real" file
        /// </summary>
        protected static Dictionary<string, List<FileError>> ReadErrorsFromFile(string fullPath, bool fromProlint, Dictionary<string, string> permutePaths) {
            var output = new Dictionary<string, List<FileError>>(StringComparer.CurrentCultureIgnoreCase);

            if (!File.Exists(fullPath))
                return output;

            var lastLineNbCouple = new[] {-10, -10};

            Utils.ForEachLine(fullPath, new byte[0], (i, line) => {
                var fields = line.Split('\t').ToList();
                if (fields.Count == 8) {
                    var compiledPath = permutePaths.ContainsKey(fields[0]) ? permutePaths[fields[0]] : fields[0];
                    // new file
                    // the path of the file that triggered the compiler error, it can be empty so we make sure to set it
                    var sourcePath = string.IsNullOrEmpty(fields[1]) ? fields[0] : fields[1];
                    sourcePath = permutePaths.ContainsKey(sourcePath) ? permutePaths[sourcePath] : sourcePath;
                    if (!output.ContainsKey(compiledPath)) {
                        output.Add(compiledPath, new List<FileError>());
                        lastLineNbCouple = new[] {-10, -10};
                    }

                    ErrorLevel errorLevel;
                    if (!Enum.TryParse(fields[2], true, out errorLevel))
                        errorLevel = ErrorLevel.Error;

                    // we store the line/error number couple because we don't want two identical messages to appear
                    var thisLineNbCouple = new[] {(int) fields[3].ConvertFromStr(typeof(int)), (int) fields[5].ConvertFromStr(typeof(int))};

                    if (thisLineNbCouple[0] == lastLineNbCouple[0] && thisLineNbCouple[1] == lastLineNbCouple[1]) {
                        // same line/error number as previously
                        if (output[compiledPath].Count > 0) {
                            var lastFileError = output[compiledPath].Last();
                            if (lastFileError != null)
                                lastFileError.Times = lastFileError.Times == 0 ? 2 : lastFileError.Times + 1;
                        }
                        return;
                    }
                    lastLineNbCouple = thisLineNbCouple;

                    var baseFileName = Path.GetFileName(sourcePath);

                    // add error
                    output[compiledPath].Add(new FileError {
                        CompiledFilePath = compiledPath,
                        SourcePath = sourcePath,
                        Level = errorLevel,
                        Line = Math.Max(0, lastLineNbCouple[0] - 1),
                        Column = Math.Max(0, (int) fields[4].ConvertFromStr(typeof(int)) - 1),
                        ErrorNumber = lastLineNbCouple[1],
                        Message = fields[6].Replace("<br>", "\n").Replace(compiledPath, baseFileName).Replace(sourcePath, baseFileName).Trim(),
                        Help = fields[7].Replace("<br>", "\n").Trim(),
                        FromProlint = fromProlint
                    });
                }
            });

            return output;
        }

        #endregion
    }

    #endregion

    #region ProExecutionCompile

    public class ProExecutionCompile : ProExecutionHandleCompilation {
        #region Life and death

        /// <summary>
        ///     Construct with the current env
        /// </summary>
        public ProExecutionCompile() : this(null) { }

        public ProExecutionCompile(Config.ProConfig proEnv) : base(proEnv) { }

        #endregion

        #region Override

        public override ExecutionType ExecutionType {
            get { return ExecutionType.Compile; }
        }

        /// <summary>
        ///     Creates a list of files to deploy after a compilation,
        ///     for each Origin file will correspond one (or more if it's a .cls) .r file,
        ///     and one .lst if the option has been checked
        /// </summary>
        protected override List<FileToDeploy> GetFilesToDeployAfterCompilation() {
            return Deployer.GetFilesToDeployAfterCompilation(this);
        }

        protected override void CheckParameters() {
            if (!ProEnv.CompileLocally && !Path.IsPathRooted(ProEnv.TargetDirectory)) throw new ExecutionParametersException("The compilation is not done near the source and the target directory is invalid : " + (string.IsNullOrEmpty(ProEnv.TargetDirectory) ? "it's empty!" : ProEnv.TargetDirectory).Quoter());
            base.CheckParameters();
        }

        protected override bool CanUseBatchMode() {
            return true;
        }

        /// <summary>
        ///     get the output directory that will be use to generate the .r (and listing debug-list...)
        /// </summary>
        protected override void ComputeOutputDir(FileToCompile fileToCompile, string localSubTempDir, int count) {
            // for *.cls files, as many *.r files are generated, we need to compile in a temp directory
            // we need to know which *.r files were generated for each input file
            // so each file gets his own sub tempDir
            var lastDeployment = ProEnv.Deployer.GetTransfersNeededForFile(fileToCompile.SourcePath, 0).Last();
            if (lastDeployment.DeployType != DeployType.Move ||
                ProEnv.CompileForceUseOfTemp ||
                Path.GetExtension(fileToCompile.SourcePath ?? "").Equals(ExtCls))
                if (lastDeployment.DeployType != DeployType.Ftp &&
                    !string.IsNullOrEmpty(ProEnv.TargetDirectory) && ProEnv.TargetDirectory.Length > 2 && !ProEnv.TargetDirectory.Substring(0, 2).EqualsCi(_localTempDir.Substring(0, 2))) {
                    if (!Directory.Exists(DistantTempDir)) {
                        var dirInfo = Directory.CreateDirectory(DistantTempDir);
                        dirInfo.Attributes |= FileAttributes.Hidden;
                    }
                    fileToCompile.CompilationOutputDir = Path.Combine(DistantTempDir, count.ToString());
                } else {
                    fileToCompile.CompilationOutputDir = localSubTempDir;
                }
            else fileToCompile.CompilationOutputDir = lastDeployment.TargetBasePath;
        }

        #endregion
    }

    #endregion
}