#region header

// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (DeploymentHandler.cs) is part of csdeployer.
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
using System.Threading;
using abldeployer.Core.Exceptions;
using abldeployer.Lib;

namespace abldeployer.Core {
    public abstract class DeploymentHandler {

        #region Life and death

        /// <summary>
        ///     Constructor
        /// </summary>
        public DeploymentHandler(Config.ProConfig proEnv) {
            _proEnv = proEnv;
            StartingTime = DateTime.Now;
        }

        #endregion

        #region Events

        /// <summary>
        ///     The action to execute just after the end of a prowin process
        /// </summary>
        public Action<DeploymentHandler> OnExecutionEnd { protected get; set; }

        /// <summary>
        ///     The action to execute at the end of the process if it went well
        /// </summary>
        public Action<DeploymentHandler> OnExecutionOk { protected get; set; }

        /// <summary>
        ///     The action to execute at the end of the process if something went wrong
        /// </summary>
        public Action<DeploymentHandler> OnExecutionFailed { protected get; set; }

        #endregion

        #region Options

        /// <summary>
        ///     If true, don't actually do anything, just test it
        /// </summary>
        public bool IsTestMode { get; set; }

        /// <summary>
        ///     When true, we activate the log just before compiling with FileId active + we generate a file that list referenced
        ///     table in the .r
        /// </summary>
        public virtual bool IsAnalysisMode { get; set; }

        #endregion

        #region Public properties

        /// <summary>
        ///     max step number composing this deployment
        /// </summary>
        public virtual int MaxStep {
            get { return _maxStep; }
        }

        /// <summary>
        ///     Current deployment step
        /// </summary>
        public int CurrentStep { get; protected set; }

        /// <summary>
        ///     Total number of operations composing this deployment
        ///     compil, deploy compil r code (step 0), step 1...
        /// </summary>
        public virtual int TotalNumberOfOperations {
            get { return MaxStep + 2; }
        }

        /// <summary>
        ///     0 -> 100% *TotalNumberOfOperations  progression for the deployment
        /// </summary>
        public virtual float OverallProgressionPercentage {
            get {
                var totalPerc = _proCompilation == null ? 0 : _proCompilation.CompilationProgression;
                if (CurrentStep > 0) totalPerc += CurrentStep * 100;
                totalPerc += _currentStepDeployPercentage;
                return totalPerc;
            }
        }

        /// <summary>
        ///     Returns the name of the current step
        /// </summary>
        public virtual DeploymentStep CurrentOperationName {
            get {
                if (CurrentStep == 0) {
                    if (_proCompilation != null && _proCompilation.CurrentNumberOfProcesses > 0) return DeploymentStep.Compilation;
                    return DeploymentStep.DeployRCode;
                }
                return DeploymentStep.DeployFile;
            }
        }

        /// <summary>
        ///     Returns the progression for the current step
        /// </summary>
        public virtual float CurrentOperationPercentage {
            get {
                if (CurrentStep == 0 && _proCompilation != null && _proCompilation.CurrentNumberOfProcesses > 0) return _proCompilation.CompilationProgression;
                return _currentStepDeployPercentage;
            }
        }

        /// <summary>
        ///     remember the time when the compilation started
        /// </summary>
        public DateTime StartingTime { get; protected set; }

        /// <summary>
        ///     Human readable amount of time needed for this execution
        /// </summary>
        public TimeSpan TotalDeploymentTime { get; protected set; }

        public bool CompilationHasFailed { get; protected set; }

        public bool HasBeenCancelled { get; protected set; }

        public List<DeploymentException> HandledExceptions { get; private set; }

        public bool DeploymentErrorOccured {
            get { return _deploymentErrorOccured; }
        }

        public Config.ProConfig ProEnv {
            get { return _proEnv; }
        }

        public MultiCompilation ProCompilation {
            get { return _proCompilation; }
        }

        public Dictionary<int, List<FileToDeploy>> FilesToDeployPerStep {
            get { return _filesToDeployPerStep; }
        }

        /// <summary>
        ///     Returns the list of all the compilation errors
        /// </summary>
        public List<FileError> CompilationErrors {
            get {
                if (_proCompilation == null)
                    return null;
                return _proCompilation.ListFilesToCompile.Where(compile => compile.Errors != null).SelectMany(compile => compile.Errors).ToNonNullList();
            }
        }

        /// <summary>
        ///     List of all the compilation errors, the absolute path are replaced with relative ones
        /// </summary>
        public List<FileError> CompilationErrorsOutput {
            get {
                var output = new List<FileError>();
                var fileErrors = CompilationErrors;
                if (fileErrors == null)
                    return null;
                foreach (var fileError in fileErrors) {
                    var newErr = fileError.Copy();
                    newErr.CompiledFilePath = newErr.CompiledFilePath.Replace(_proEnv.SourceDirectory.CorrectDirPath(), "");
                    newErr.SourcePath = newErr.SourcePath.Replace(_proEnv.SourceDirectory.CorrectDirPath(), "");
                    output.Add(newErr);
                }
                return output;
            }
        }

        #endregion

        #region protected fields

        protected Dictionary<int, List<FileToDeploy>> _filesToDeployPerStep = new Dictionary<int, List<FileToDeploy>>();

        protected Config.ProConfig _proEnv;

        protected volatile float _currentStepDeployPercentage;

        // Stores the current compilation info
        protected MultiCompilation _proCompilation;

        protected CancellationTokenSource _cancelSource = new CancellationTokenSource();

        protected ProExecutionDeploymentHook _hookExecution;
        protected int _maxStep;

        protected bool _deploymentErrorOccured = false;

        #endregion

        #region Public

        /// <summary>
        ///     Start the deployment
        /// </summary>
        public void Start() {
            StartingTime = DateTime.Now;
            _maxStep = _proEnv.Deployer.DeployTransferRules.Count > 0 ? _proEnv.Deployer.DeployTransferRules.Max(rule => rule.Step) : 0;
            _filesToDeployPerStep.Clear();

            // new mass compilation
            _proCompilation = new MultiCompilation(_proEnv) {
                // check if we need to force the compiler to only use 1 process 
                // (either because the user want to, or because we have a single user mode database)
                MonoProcess = _proEnv.ForceSingleProcess || _proEnv.IsDatabaseSingleUser,
                NumberOfProcessesPerCore = _proEnv.NumberProcessPerCore,
                RFilesOnly = _proEnv.OnlyGenerateRcode,
                IsTestMode = IsTestMode,
                IsAnalysisMode = IsAnalysisMode
            };

            _proCompilation.OnCompilationOk += OnCompilationOk;
            _proCompilation.OnCompilationFailed += OnCompilationFailed;

            // compile files that have a transfer planned
            BeforeStarting();
            _proCompilation.CompileFiles(
                GetFilesToCompileInStepZero()
                    .Where(toCompile => _proEnv.Deployer.GetTransfersNeededForFile(toCompile.SourcePath, 0).Count > 0)
                    .ToNonNullList()
            );
        }

        /// <summary>
        ///     Call this method to cancel the execution of this deployment
        /// </summary>
        public virtual void Cancel() {
            HasBeenCancelled = true;
            _cancelSource.Cancel();
            if (_proCompilation != null)
                _proCompilation.CancelCompilation();
            if (_hookExecution != null)
                _hookExecution.KillProcess();
            EndOfDeployment();
        }

        #endregion

        #region To override

        /// <summary>
        ///     Called just before calling the end of deployment events and once everything is done
        /// </summary>
        protected virtual void BeforeEndOfSuccessfulDeployment() {
            EndOfDeployment();
        }

        /// <summary>
        ///     Do stuff before starting the treatment
        /// </summary>
        /// <returns></returns>
        /// <exception cref="DeploymentException"></exception>
        protected virtual void BeforeStarting() { }

        /// <summary>
        ///     List all the compilable files in the source directory
        /// </summary>
        protected virtual List<FileToCompile> GetFilesToCompileInStepZero() {
            return
                GetFilteredFilesList(_proEnv.SourceDirectory, 0, _proEnv.ExploreRecursively ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly, _proEnv.FilesPatternCompilable)
                    .Select(s => new FileToCompile(s))
                    .ToNonNullList();
        }

        /// <summary>
        ///     List all the files that should be deployed from the source directory
        /// </summary>
        protected virtual List<FileToDeploy> GetFilesToDeployInStepOne() {
            // list files
            var outlist = GetFilteredFilesList(_proEnv.SourceDirectory, 1, _proEnv.ExploreRecursively ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .SelectMany(file => _proEnv.Deployer.GetTransfersNeededForFile(file, 1))
                .ToNonNullList();
            return outlist;
        }

        /// <summary>
        ///     List all the files that should be deployed from the source directory
        /// </summary>
        protected virtual List<FileToDeploy> GetFilesToDeployInStepTwoAndMore(int currentStep) {
            // list files
            var outlist = GetFilteredFilesList(_proEnv.TargetDirectory, currentStep, _proEnv.ExploreRecursively ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .SelectMany(file => _proEnv.Deployer.GetTransfersNeededForFile(file, currentStep))
                .ToNonNullList();
            // list folders
            outlist.AddRange(GetFilteredFoldersList(_proEnv.TargetDirectory, currentStep, _proEnv.ExploreRecursively ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .SelectMany(folder => _proEnv.Deployer.GetTransfersNeededForFolders(folder, currentStep))
                .ToNonNullList());
            return outlist;
        }

        /// <summary>
        ///     Deploys the list of files
        /// </summary>
        protected virtual List<FileToDeploy> Deployfiles(List<FileToDeploy> filesToDeploy) {
            if (IsTestMode) {
                filesToDeploy.ForEach(deploy => deploy.IsOk = true);
                return filesToDeploy;
            }
            return _proEnv.Deployer.DeployFiles(filesToDeploy, f => _currentStepDeployPercentage = f, _cancelSource);
        }

        #endregion

        #region protected

        /// <summary>
        ///     Returns a list of folders in the given folder (recursively or not depending on the option),
        ///     this list is filtered thanks to the filtered rules
        /// </summary>
        protected virtual List<string> GetFilteredFoldersList(string folder, int step, SearchOption searchOptions) {
            if (!Directory.Exists(folder))
                return new List<string>();
            return _proEnv.Deployer.GetFilteredList(Directory.EnumerateDirectories(folder, "*", searchOptions), step).ToList();
        }

        /// <summary>
        ///     Returns a list of files in the given folder (recursively or not depending on the option),
        ///     this list is filtered thanks to the filtered rules
        /// </summary>
        protected virtual List<string> GetFilteredFilesList(string folder, int step, SearchOption searchOptions, string fileExtensionFilter = "*") {
            if (!Directory.Exists(folder))
                return new List<string>();
            return _proEnv.Deployer.GetFilteredList
            (
                fileExtensionFilter
                    .Split(',')
                    .SelectMany(searchPattern => Directory.EnumerateFiles(folder, searchPattern, searchOptions)),
                step
            ).ToList();
        }

        /// <summary>
        ///     Called when the compilation step 0 failed
        /// </summary>
        protected void OnCompilationFailed(MultiCompilation proCompilation) {
            if (HasBeenCancelled)
                return;

            CompilationHasFailed = true;
            EndOfDeployment();
        }

        /// <summary>
        ///     Called when the compilation step 0 ended correctly
        /// </summary>
        protected void OnCompilationOk(MultiCompilation comp, List<FileToCompile> fileToCompiles, List<FileToDeploy> filesToDeploy) {
            if (HasBeenCancelled)
                return;

            // Make the deployment for the compilation step (0)
            try {
                _filesToDeployPerStep.Add(0, Deployfiles(filesToDeploy));
            } catch (Exception e) {
                AddHandledExceptions(e);
            }

            comp.Clean();

            // Make the deployment for the step 1 and >=
            ExecuteDeploymentHook(0);
        }

        /// <summary>
        ///     Deployment for the step 1 and >=
        /// </summary>
        protected virtual void DeployStepOneAndMore(int currentStep) {
            if (HasBeenCancelled)
                return;

            _currentStepDeployPercentage = 0;
            CurrentStep = currentStep;

            if (currentStep <= MaxStep) {
                try {
                    var filesToDeploy = currentStep == 1 ? GetFilesToDeployInStepOne() : GetFilesToDeployInStepTwoAndMore(currentStep);
                    _filesToDeployPerStep.Add(currentStep, Deployfiles(filesToDeploy));
                } catch (Exception e) {
                    AddHandledExceptions(e);
                }

                // hook
                if (!HasBeenCancelled)
                    ExecuteDeploymentHook(currentStep);
            } else {
                // end of the overall deployment
                BeforeEndOfSuccessfulDeployment();
            }
        }

        /// <summary>
        ///     Execute the hook procedure for the step 0+
        /// </summary>
        protected void ExecuteDeploymentHook(int currentStep) {
            if (HasBeenCancelled)
                return;

            currentStep++;

            // launch the compile process for the current file (if any)
            if (File.Exists(_proEnv.FileDeploymentHook))
                try {
                    _hookExecution = new ProExecutionDeploymentHook(_proEnv) {
                        DeploymentStep = currentStep - 1,
                        DeploymentSourcePath = _proEnv.SourceDirectory,
                        NoBatch = true,
                        NeedDatabaseConnection = true
                    };
                    _hookExecution.OnExecutionEnd += execution => { DeployStepOneAndMore(currentStep); };
                    _hookExecution.Start();
                    return;
                } catch (Exception e) {
                    AddHandledExceptions(e);
                }

            DeployStepOneAndMore(currentStep);
        }

        /// <summary>
        ///     This method is executed when the overall execution is over
        /// </summary>
        protected virtual void EndOfDeployment() {
            TotalDeploymentTime = TimeSpan.FromMilliseconds(DateTime.Now.Subtract(StartingTime).TotalMilliseconds);

            if (_proCompilation != null) foreach (var exception in _proCompilation.HandledExceptions) AddHandledExceptions(exception);

            try {
                if (!HasBeenCancelled && !CompilationHasFailed && !_deploymentErrorOccured) {
                    if (OnExecutionOk != null)
                        OnExecutionOk(this);
                } else {
                    if (OnExecutionFailed != null)
                        OnExecutionFailed(this);
                }
            } catch (Exception e) {
                AddHandledExceptions(e);
            }

            try {
                if (OnExecutionEnd != null)
                    OnExecutionEnd(this);
            } catch (Exception e) {
                AddHandledExceptions(e);
            }
        }

        protected void AddHandledExceptions(Exception exception, string customMessage = null) {
            if (HandledExceptions == null)
                HandledExceptions = new List<DeploymentException>();
            if (customMessage != null)
                HandledExceptions.Add(new DeploymentException(customMessage, exception));
            else HandledExceptions.Add(new DeploymentException("Deployment exception", exception));
        }

        #endregion
    }
}