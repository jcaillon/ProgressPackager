﻿#region header
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
using System.Text;
using System.Threading;
using csdeployer.Lib;

namespace csdeployer.Core.Deploy {
    internal abstract class DeploymentHandler {
        #region Events

        /// <summary>
        /// The action to execute just after the end of a prowin process
        /// </summary>
        public Action<DeploymentHandler> OnExecutionEnd { protected get; set; }

        /// <summary>
        /// The action to execute at the end of the process if it went well
        /// </summary>
        public Action<DeploymentHandler> OnExecutionOk { protected get; set; }

        /// <summary>
        /// The action to execute at the end of the process if something went wrong
        /// </summary>
        public Action<DeploymentHandler> OnExecutionFailed { protected get; set; }

        #endregion

        #region Options

        /// <summary>
        /// If true, don't actually do anything, just test it
        /// </summary>
        public bool IsTestMode { get; set; }

        /// <summary>
        /// When true, we activate the log just before compiling with FileId active + we generate a file that list referenced table in the .r
        /// </summary>
        public virtual bool IsAnalysisMode { get; set; }

        #endregion

        #region Public properties

        /// <summary>
        /// max step number composing this deployment
        /// </summary>
        public virtual int MaxStep {
            get { return _maxStep; }
        }

        /// <summary>
        /// Current deployment step
        /// </summary>
        public int CurrentStep { get; protected set; }

        /// <summary>
        /// Total number of operations composing this deployment
        /// compil, deploy compil r code (step 0), step 1...
        /// </summary>
        public virtual int TotalNumberOfOperations {
            get { return MaxStep + 2; }
        }

        /// <summary>
        /// 0 -> 100% *TotalNumberOfOperations  progression for the deployment
        /// </summary>
        public virtual float OverallProgressionPercentage {
            get {
                float totalPerc = _proCompilation == null ? 0 : _proCompilation.CompilationProgression;
                if (CurrentStep > 0) {
                    totalPerc += CurrentStep * 100;
                }
                totalPerc += _currentStepDeployPercentage;
                return totalPerc;
            }
        }

        /// <summary>
        /// Returns the name of the current step
        /// </summary>
        public virtual string CurrentOperationName {
            get {
                if (CurrentStep == 0) {
                    if (_proCompilation != null && _proCompilation.CurrentNumberOfProcesses > 0) {
                        return "Compilation";
                    }
                    return "Déploiement des r-codes";
                }
                return "Déploiement, étape " + CurrentStep;
            }
        }

        /// <summary>
        /// Returns the progression for the current step
        /// </summary>
        public virtual float CurrentOperationPercentage {
            get {
                if (CurrentStep == 0 && _proCompilation != null && _proCompilation.CurrentNumberOfProcesses > 0) {
                    return _proCompilation.CompilationProgression;
                }
                return _currentStepDeployPercentage;
            }
        }

        /// <summary>
        /// remember the time when the compilation started
        /// </summary>
        public DateTime StartingTime { get; protected set; }

        /// <summary>
        /// Human readable amount of time needed for this execution
        /// </summary>
        public string TotalDeploymentTime { get; protected set; }

        public bool CompilationHasFailed { get; protected set; }

        public bool HasBeenCancelled { get; protected set; }

        /// <summary>
        /// Get the time elapsed since the beginning of the compilation in a human readable format
        /// </summary>
        public string ElapsedTime {
            get { return Utils.ConvertToHumanTime(TimeSpan.FromMilliseconds(DateTime.Now.Subtract(StartingTime).TotalMilliseconds)); }
        }

        /// <summary>
        /// Returns the list of all the compilation errors
        /// </summary>
        public List<FileError> CompilationErrors {
            get {
                if (_proCompilation == null)
                    return null;
                return _proCompilation.ListFilesToCompile.Where(compile => compile.Errors != null).SelectMany(compile => compile.Errors).ToNonNullList();
            }
        }

        /// <summary>
        /// List of all the compilation errors, the absolute path are replaced with relative ones
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

        #region Life and death

        /// <summary>
        /// Constructor
        /// </summary>
        public DeploymentHandler(Config.ProConfig proEnv) {
            _proEnv = proEnv ?? Config.Instance;
            StartingTime = DateTime.Now;
        }

        #endregion

        #region Public

        /// <summary>
        /// Start the deployment
        /// </summary>
        public bool Start() {
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
            return BeforeStarting() && _proCompilation.CompileFiles(
                       GetFilesToCompileInStepZero()
                           .Where(toCompile => _proEnv.Deployer.GetTransfersNeededForFile(toCompile.SourcePath, 0).Count > 0)
                           .ToNonNullList()
                   );
        }

        /// <summary>
        /// Call this method to cancel the execution of this deployment
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
        /// Called just before calling the end of deployment events and once everything is done
        /// </summary>
        protected virtual void BeforeEndOfSuccessfulDeployment() {
            EndOfDeployment();
        }

        /// <summary>
        /// Do stuff before starting the treatment, returns false if we shouldn't start the treatment
        /// </summary>
        /// <returns></returns>
        protected virtual bool BeforeStarting() {
            return true;
        }

        /// <summary>
        /// List all the compilable files in the source directory
        /// </summary>
        protected virtual List<FileToCompile> GetFilesToCompileInStepZero() {
            return
                GetFilteredFilesList(_proEnv.SourceDirectory, 0, _proEnv.ExploreRecursively ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly, _proEnv.FilesPatternCompilable)
                    .Select(s => new FileToCompile(s))
                    .ToNonNullList();
        }

        /// <summary>
        /// List all the files that should be deployed from the source directory
        /// </summary>
        protected virtual List<FileToDeploy> GetFilesToDeployInStepOne() {
            // list files
            var outlist = GetFilteredFilesList(_proEnv.SourceDirectory, 1, _proEnv.ExploreRecursively ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .SelectMany(file => _proEnv.Deployer.GetTransfersNeededForFile(file, 1))
                .ToNonNullList();
            return outlist;
        }

        /// <summary>
        /// List all the files that should be deployed from the source directory
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
        /// Deploys the list of files
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
        /// Returns a list of folders in the given folder (recursively or not depending on the option),
        /// this list is filtered thanks to the filtered rules
        /// </summary>
        protected virtual List<string> GetFilteredFoldersList(string folder, int step, SearchOption searchOptions) {
            if (!Directory.Exists(folder))
                return new List<string>();
            return _proEnv.Deployer.GetFilteredList(Directory.EnumerateDirectories(folder, "*", searchOptions), step).ToList();
        }

        /// <summary>
        /// Returns a list of files in the given folder (recursively or not depending on the option),
        /// this list is filtered thanks to the filtered rules
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
        /// Called when the compilation step 0 failed
        /// </summary>
        protected void OnCompilationFailed(MultiCompilation proCompilation) {
            if (HasBeenCancelled)
                return;

            CompilationHasFailed = true;
            EndOfDeployment();
        }

        /// <summary>
        /// Called when the compilation step 0 ended correctly
        /// </summary>
        protected void OnCompilationOk(MultiCompilation comp, List<FileToCompile> fileToCompiles, List<FileToDeploy> filesToDeploy) {
            if (HasBeenCancelled)
                return;
            // Make the deployment for the compilation step (0)
            try {
                _filesToDeployPerStep.Add(0, Deployfiles(filesToDeploy));
            } catch (Exception e) {
                ErrorHandler.LogErrors(e);
            }

            comp.Clean();

            // Make the deployment for the step 1 and >=
            ExecuteDeploymentHook(0);
        }

        /// <summary>
        /// Deployment for the step 1 and >=
        /// </summary>
        protected virtual void DeployStepOneAndMore(int currentStep) {
            if (HasBeenCancelled)
                return;

            _currentStepDeployPercentage = 0;
            CurrentStep = currentStep;

            if (currentStep <= MaxStep) {
                try {
                    List<FileToDeploy> filesToDeploy = currentStep == 1 ? GetFilesToDeployInStepOne() : GetFilesToDeployInStepTwoAndMore(currentStep);
                    _filesToDeployPerStep.Add(currentStep, Deployfiles(filesToDeploy));
                } catch (Exception e) {
                    ErrorHandler.LogErrors(e);
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
        /// Execute the hook procedure for the step 0+
        /// </summary>
        protected void ExecuteDeploymentHook(int currentStep) {
            if (HasBeenCancelled)
                return;

            currentStep++;

            // launch the compile process for the current file (if any)
            if (File.Exists(_proEnv.FileDeploymentHook)) {
                try {
                    _hookExecution = new ProExecutionDeploymentHook(_proEnv) {
                        DeploymentStep = currentStep - 1,
                        DeploymentSourcePath = _proEnv.SourceDirectory,
                        NoBatch = true,
                        NeedDatabaseConnection = true
                    };
                    _hookExecution.OnExecutionEnd += execution => { DeployStepOneAndMore(currentStep); };
                    if (!_hookExecution.Start()) {
                        DeployStepOneAndMore(currentStep);
                    }
                    return;
                } catch (Exception e) {
                    ErrorHandler.LogErrors(e);
                }
            }

            DeployStepOneAndMore(currentStep);
        }

        /// <summary>
        /// This method is executed when the overall execution is over
        /// </summary>
        protected virtual void EndOfDeployment() {
            TotalDeploymentTime = ElapsedTime;

            try {
                if (!HasBeenCancelled && !CompilationHasFailed && !_deploymentErrorOccured) {
                    if (OnExecutionOk != null)
                        OnExecutionOk(this);
                } else {
                    if (OnExecutionFailed != null)
                        OnExecutionFailed(this);
                }
            } catch (Exception e) {
                ErrorHandler.LogErrors(e);
            }

            try {
                if (OnExecutionEnd != null)
                    OnExecutionEnd(this);
            } catch (Exception e) {
                ErrorHandler.LogErrors(e);
            }
        }

        #endregion

        #region FormatDeploymentReport

        /// <summary>
        /// Generate an html report for the current deployment
        /// </summary>
        public virtual string FormatDeploymentParameters() {
            return @"             
                <h2>Paramètres du déploiement :</h2>
                <div class='IndentDiv'>
                    <div>Date de début de déploiement : <b>" + StartingTime + @"</b></div>
                    <div>Date de début de compilation : <b>" + _proCompilation.StartingTime + @"</b></div>
                    <div>Nombre de processeurs sur cet ordinateur : <b>" + Environment.ProcessorCount + @"</b></div>
                    <div>Nombre de process progress utilisés pour la compilation : <b>" + _proCompilation.TotalNumberOfProcesses + @"</b></div>
                    <div>Compilation forcée en mono-process? : <b>" + _proCompilation.MonoProcess + (_proEnv.IsDatabaseSingleUser ? " (connecté à une base de données en mono-utilisateur!)" : "") + @"</b></div>
                    <div>Répertoire des sources : " + _proEnv.SourceDirectory.ToHtmlLink() + @"</div>
                    <div>Répertoire cible pour le déploiement : " + _proEnv.TargetDirectory.ToHtmlLink() + @"</div>       
                </div>";
        }

        /// <summary>
        /// Generate an html report for the current deployment
        /// </summary>
        public virtual string FormatDeploymentResults() {
            StringBuilder currentReport = new StringBuilder();

            currentReport.Append(@"<h2>Détails sur le déploiement :</h2>");
            currentReport.Append(@"<div class='IndentDiv'>");

            if (HasBeenCancelled) {
                // the process has been canceled
                currentReport.Append(@"<div><img style='padding-right: 20px;' src='Warning_25x25' height='15px'>Déploiement annulé par l'utilisateur</div>");
            } else if (CompilationHasFailed) {
                // provide info on the possible error!
                currentReport.Append(@"<div><img style='padding-right: 20px;' src='Error_25x25' height='15px'>Un process progress a fini en erreur, déploiement arrêté</div>");

                if (_proCompilation.CompilationFailedOnMaxUser) {
                    currentReport.Append(@"<div><img style='padding-right: 20px;' src='Help_25x25' height='15px'>One or more processes started for this compilation tried to connect to the database and failed because the maximum number of connection has been reached (error 748). To correct this problem, you can either :<br><li>reduce the number of processes to use for each core of your computer</li><li>or increase the maximum of connections for your database (-n parameter in the PROSERVE command)</li></div>");
                }
            } else if (_deploymentErrorOccured) {
                currentReport.Append(@"<div><img style='padding-right: 20px;' src='Error_25x25' height='15px'>Le déploiement a échoué</div>");
            }

            var listLinesCompilation = new List<Tuple<int, string>>();
            StringBuilder line = new StringBuilder();

            var totalDeployedFiles = 0;
            var nbDeploymentError = 0;
            var nbCompilationError = 0;
            var nbCompilationWarning = 0;

            // compilation errors
            foreach (var fileInError in _proCompilation.ListFilesToCompile.Where(file => file.Errors != null)) {
                bool hasError = fileInError.Errors.Exists(error => error.Level >= ErrorLevel.Error);
                bool hasWarning = fileInError.Errors.Exists(error => error.Level < ErrorLevel.Error);

                if (hasError || hasWarning) {
                    // only add compilation errors
                    line.Clear();
                    line.Append("<div %ALTERNATE%style=\"background-repeat: no-repeat; background-image: url('" + (hasError ? "Error_25x25" : "Warning_25x25") + "'); padding-left: 35px; padding-top: 6px; padding-bottom: 6px;\">");
                    line.Append(ProExecutionCompile.FormatCompilationResultForSingleFile(fileInError.SourcePath, fileInError, null));
                    line.Append("</div>");
                    listLinesCompilation.Add(new Tuple<int, string>(hasError ? 3 : 2, line.ToString()));
                }

                if (hasError) {
                    nbCompilationError++;
                } else if (hasWarning)
                    nbCompilationWarning++;
            }

            // for each deploy step
            var listLinesByStep = new Dictionary<int, List<Tuple<int, string>>> {
                {0, new List<Tuple<int, string>>()}
            };
            foreach (var kpv in _filesToDeployPerStep) {
                // group either by directory name or by pack name
                var groupDirectory = kpv.Value.GroupBy(deploy => deploy.GroupKey).Select(deploys => deploys.ToList()).ToList();

                foreach (var group in groupDirectory.OrderByDescending(list => list.First().DeployType).ThenBy(list => list.First().GroupKey)) {
                    var deployFailed = group.Exists(deploy => !deploy.IsOk);
                    var first = group.First();

                    line.Clear();
                    line.Append("<div %ALTERNATE%style=\"background-repeat: no-repeat; background-image: url('" + (deployFailed ? "Error_25x25" : "Ok_25x25") + "'); padding-left: 35px; padding-top: 6px; padding-bottom: 6px;\">");
                    line.Append(first.ToStringGroupHeader());
                    foreach (var fileToDeploy in group.OrderBy(deploy => deploy.To)) {
                        line.Append(fileToDeploy.ToStringDescription(kpv.Key <= 1 ? _proEnv.SourceDirectory : _proEnv.TargetDirectory));
                    }
                    line.Append("</div>");

                    if (!listLinesByStep.ContainsKey(kpv.Key))
                        listLinesByStep.Add(kpv.Key, new List<Tuple<int, string>>());

                    listLinesByStep[kpv.Key].Add(new Tuple<int, string>(deployFailed ? 3 : 1, line.ToString()));

                    if (deployFailed)
                        nbDeploymentError += group.Count(deploy => !deploy.IsOk);
                    else
                        totalDeployedFiles += group.Count;
                }
            }

            // compilation
            currentReport.Append(@"<div style='padding-top: 7px; padding-bottom: 7px;'>Nombre de fichiers compilés : <b>" + _proCompilation.NbFilesToCompile + "</b>, répartition : " + Utils.GetNbFilesPerType(_proCompilation.ListFilesToCompile.Select(compile => compile.SourcePath).ToList()).Aggregate("", (current, kpv) => current + (@"<img style='padding-right: 5px;' src='" + Utils.GetExtensionImage(kpv.Key.ToString(), true) + "' height='15px'><span style='padding-right: 12px;'>x" + kpv.Value + "</span>")) + "</div>");

            // compilation time
            currentReport.Append(@"<div><img style='padding-right: 20px;' src='Clock_15px' height='15px'>Temps de compilation total : <b>" + _proCompilation.TotalCompilationTime + @"</b></div>");

            if (nbCompilationError > 0)
                currentReport.Append("<div><img style='padding-right: 20px;' src='Error_25x25' height='15px'>Nombre de fichiers avec erreur(s) de compilation : " + nbCompilationError + "</div>");
            if (nbCompilationWarning > 0)
                currentReport.Append("<div><img style='padding-right: 20px;' src='Warning_25x25' height='15px'>Nombre de fichiers avec avertissement(s) de compilation : " + nbCompilationWarning + "</div>");
            if (_proCompilation.NumberOfFilesTreated - nbCompilationError - nbCompilationWarning > 0)
                currentReport.Append("<div><img style='padding-right: 20px;' src='Ok_25x25' height='15px'>Nombre de fichiers compilés correctement : " + (_proCompilation.NumberOfFilesTreated - nbCompilationError - nbCompilationWarning) + "</div>");

            // deploy
            currentReport.Append(@"<div style='padding-top: 7px; padding-bottom: 7px;'>Nombre de fichiers déployés : <b>" + totalDeployedFiles + "</b>, répartition : " + Utils.GetNbFilesPerType(_filesToDeployPerStep.SelectMany(pair => pair.Value).Select(deploy => deploy.To).ToList()).Aggregate("", (current, kpv) => current + (@"<img style='padding-right: 5px;' src='" + Utils.GetExtensionImage(kpv.Key.ToString(), true) + "' height='15px'><span style='padding-right: 12px;'>x" + kpv.Value + "</span>")) + "</div>");

            // deployment time
            currentReport.Append(@"<div><img style='padding-right: 20px;' src='Clock_15px' height='15px'>Temps de déploiement total : <b>" + TotalDeploymentTime + @"</b></div>");

            if (nbDeploymentError > 0)
                currentReport.Append("<div><img style='padding-right: 20px;' src='Error_25x25' height='15px'>Nombre de fichiers avec erreur(s) de déploiement : " + nbDeploymentError + "</div>");
            if (totalDeployedFiles - nbDeploymentError > 0)
                currentReport.Append("<div><img style='padding-right: 20px;' src='Ok_25x25' height='15px'>Nombre de fichiers déployés correctement : " + (totalDeployedFiles - nbDeploymentError) + "</div>");

            // compilation
            if (listLinesCompilation.Count > 0) {
                currentReport.Append("<h3>Détails des erreurs/avertissements de compilation :</h3>");
                var boolAlternate = false;
                foreach (var listLine in listLinesCompilation.OrderByDescending(tuple => tuple.Item1)) {
                    currentReport.Append(listLine.Item2.Replace("%ALTERNATE%", boolAlternate ? "class='AlternatBackColor' " : "class='NormalBackColor' "));
                    boolAlternate = !boolAlternate;
                }
            }

            // deployment steps
            foreach (var listLinesKpv in listLinesByStep.Where(pair => pair.Value != null && pair.Value.Count > 0)) {
                currentReport.Append("<h3>Détails sur l'étape " + listLinesKpv.Key + " du déploiement :</h3>");
                var boolAlternate2 = false;
                foreach (var listLine in listLinesKpv.Value.OrderByDescending(tuple => tuple.Item1)) {
                    currentReport.Append(listLine.Item2.Replace("%ALTERNATE%", boolAlternate2 ? "class='AlternatBackColor' " : "class='NormalBackColor' "));
                    boolAlternate2 = !boolAlternate2;
                }
            }

            currentReport.Append(@"</div>");

            return currentReport.ToString();
        }

        #endregion
    }
}