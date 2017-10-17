#region header
// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (DeploymentHandlerDifferential.cs) is part of csdeployer.
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
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using csdeployer.Lib;

namespace csdeployer.Core.Deploy {
    /// <summary>
    /// This type of deployment handles the difference between the current source directory state and an older state
    /// </summary>
    internal class DeploymentHandlerDifferential : DeploymentHandler {
        #region Properties

        /// <summary>
        /// max step number composing this deployment, we ensure it to be at least 2
        /// </summary>
        public override int MaxStep {
            get { return Math.Max(_maxStep, 2); }
        }

        /// <summary>
        /// We add an operation, that will be : listing all the source files
        /// </summary>
        public override int TotalNumberOfOperations {
            get { return base.TotalNumberOfOperations + 1; }
        }

        /// <summary>
        /// add the listing operation
        /// </summary>
        public override float OverallProgressionPercentage {
            get { return base.OverallProgressionPercentage + _listingPercentage; }
        }

        /// <summary>
        /// Returns the name of the current step
        /// </summary>
        public override string CurrentOperationName {
            get { return _isListing ? "Listing des fichiers sources" : base.CurrentOperationName; }
        }

        /// <summary>
        /// Returns the progression for the current step
        /// </summary>
        public override float CurrentOperationPercentage {
            get { return _isListing ? _listingPercentage : base.CurrentOperationPercentage; }
        }

        /// <summary>
        /// When true, we activate the log just before compiling with FileId active + we generate a file that list referenced table in the .r
        /// </summary>
        public override bool IsAnalysisMode {
            get { return true; }
        }

        /// <summary>
        /// This returns a serializable list of files that were compiled/deployed during this deployment 
        /// </summary>
        public List<FileDeployed> DeployedFilesOutput {
            get {
                if (_deployedFilesOutput == null) {
                    var list = new Dictionary<string, FileDeployed>(StringComparer.CurrentCultureIgnoreCase);

                    // we will save data on each file that was deployed from the source directory
                    foreach (var kpv in _filesToDeployPerStep.Where(kpv => kpv.Key <= 2)) {
                        var step = kpv.Key;
                        foreach (var fileDeployed in kpv.Value) {
                            if (step == 2 && !fileDeployed.IsDeletion) {
                                continue;
                            }

                            FileDeployed listFile;
                            if (!list.ContainsKey(fileDeployed.Origin)) {
                                var compiledFile = step > 0 ? null : _proCompilation.ListFilesToCompile.Find(compile => compile.SourcePath.Equals(fileDeployed.Origin));
                                if (compiledFile != null) {
                                    listFile = new FileDeployedCompiled();
                                    var newCompiledFile = (FileDeployedCompiled) listFile;
                                    // we only keep required files located in the source directory
                                    newCompiledFile.RequiredFiles = compiledFile.RequiredFiles == null ? null : compiledFile.RequiredFiles.Where(path => !string.IsNullOrEmpty(path) && path.StartsWith(_proEnv.SourceDirectory)).Select(GetSourceFileBaseInfo).Where(info => info != null).ToList();
                                    // we only keep user's tables
                                    newCompiledFile.RequiredTables = compiledFile.RequiredTables.Where(table => !table.QualifiedTableName.Contains("._")).ToList();
                                } else {
                                    listFile = new FileDeployed();
                                }

                                listFile.SourcePath = fileDeployed.Origin;
                                var fileInf = GetSourceFileBaseInfo(fileDeployed.Origin);
                                if (fileInf != null) {
                                    listFile.Size = fileInf.Size;
                                    listFile.LastWriteTime = fileInf.LastWriteTime;
                                    listFile.Md5 = fileInf.Md5;
                                }
                                list.Add(fileDeployed.Origin, listFile);
                            } else {
                                listFile = list[fileDeployed.Origin];
                            }

                            if (fileDeployed.IsDeletion) {
                                listFile.Action = DeploymentAction.Deleted;
                            } else {
                                if (_prevSourceFiles.ContainsKey(listFile.SourcePath)) {
                                    listFile.Action = DeploymentAction.Replaced;
                                } else {
                                    listFile.Action = DeploymentAction.Added;
                                }
                            }

                            if (listFile.Targets == null) {
                                listFile.Targets = new List<DeploymentTarget>();
                            }

                            // we add a new deployment target for this file (only if it is deployed in the target dir)
                            var fileToDeployInPack = fileDeployed as FileToDeployInPack;
                            listFile.Targets.Add(new DeploymentTarget {
                                TargetPath = fileDeployed.To,
                                TargetPackPath = fileToDeployInPack == null ? null : fileToDeployInPack.PackPath,
                                TargetPathInPack = fileToDeployInPack == null ? null : fileToDeployInPack.RelativePathInPack,
                                DeployType = fileDeployed.DeployType
                            });
                        }
                    }

                    // for each file previously deployed that has not been deployed again, we also keep the data
                    foreach (var file in _sourceFilesUpToDate
                        .Where(path => _prevSourceFiles.ContainsKey(path) && !list.ContainsKey(path))
                        .Select(path => _prevSourceFiles[path])
                        .Where(file => file is FileDeployed)
                        .Cast<FileDeployed>()) {
                        list.Add(file.SourcePath, file);
                        file.Action = DeploymentAction.Existing;
                        // update the md5 value?
                        if (ComputeMd5 && (file.Md5 == null || file.Md5.Length == 0)) {
                            file.Md5 = GetSourceFileBaseInfo(file.SourcePath).Md5;
                        }
                    }

                    _deployedFilesOutput = ConvertToRelativePath(list.Values.ToList());
                }

                return _deployedFilesOutput;
            }
        }

        /// <summary>
        /// Optional list of source files (compiled/deployed) of the last deployment, needed
        /// if you want to be able to compute the difference with the current source dir state
        /// Note : the objects in this list WILL BE MODIFIED, make sure to feed a hard copy
        /// </summary>
        public List<FileDeployed> PreviousDeployedFiles {
            get { return _previousDeployedFiles; }
            set {
                if (value == null)
                    return;
                // we make each path absolute
                _previousDeployedFiles = ConvertToAbsolutePath(value.Where(deployed => deployed.Action != DeploymentAction.Deleted).ToNonNullList());
                _prevSourceFiles.Clear();
                foreach (var file in _previousDeployedFiles
                    .Where(file => !_prevSourceFiles.ContainsKey(file.SourcePath))) {
                    _prevSourceFiles.Add(file.SourcePath, file);
                }
                foreach (var file in _previousDeployedFiles
                    .Where(file => file is FileDeployedCompiled)
                    .Cast<FileDeployedCompiled>()
                    .SelectMany(file => file.RequiredFiles)
                    .Where(file => file != null && !_prevSourceFiles.ContainsKey(file.SourcePath))) {
                    _prevSourceFiles.Add(file.SourcePath, file);
                }
            }
        }

        /// <summary>
        /// Set to true to compile/deploy everything instead of just the differences
        /// </summary>
        public bool ForceFullDeploy { get; set; }

        /// <summary>
        /// Indicates whether or not we should compute the MD5 for each file in the source directory
        /// Otherwise, we base our difference only on the last write date + size
        /// </summary>
        public bool ComputeMd5 { get; set; }

        #endregion

        #region Fields

        /// <summary>
        /// List of all the files previously in the source dir
        /// </summary>
        private List<FileDeployed> _previousDeployedFiles;

        /// <summary>
        /// data for each source file that existed (and were used) in the previous deployment
        /// </summary>
        private Dictionary<string, FileSourceInfo> _prevSourceFiles = new Dictionary<string, FileSourceInfo>(StringComparer.CurrentCultureIgnoreCase);

        /// <summary>
        /// Keep data for each current source file
        /// </summary>
        private Dictionary<string, FileSourceInfo> _sourceFiles = new Dictionary<string, FileSourceInfo>(StringComparer.CurrentCultureIgnoreCase);

        /// <summary>
        /// A list of all the files that were already deployed last time and that didn't change since
        /// </summary>
        private HashSet<string> _sourceFilesUpToDate = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        /// <summary>
        /// A list of all the new files (that either didn't exist in the previous deployment or that were modified)
        /// </summary>
        private HashSet<string> _sourceFilesNew = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        /// <summary>
        /// A list of all the files that are missing compared to the previous deployment
        /// </summary>
        private HashSet<string> _sourceFilesMissing = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        /// <summary>
        /// Files deployed during this deployment
        /// </summary>
        private List<FileDeployed> _deployedFilesOutput;

        private float _listingPercentage;

        private bool _isListing;

        #endregion

        #region Life and death

        /// <summary>
        /// Constructor
        /// </summary>
        public DeploymentHandlerDifferential(Config.ProConfig proEnv) : base(proEnv) {
            ForceFullDeploy = proEnv.ForceFullDeploy;
            ComputeMd5 = proEnv.ComputeMd5;
            IsTestMode = proEnv.IsTestMode;
        }

        #endregion

        #region Override

        /// <summary>
        /// Do stuff before starting the treatment, returns false if we shouldn't start the treatment
        /// </summary>
        protected override bool BeforeStarting() {
            // creates a list of all the files in the source directory, gather info on each file
            return ListAllFilesInSourceDir();
        }

        /// <summary>
        /// Make the list of all the files that need to be (re)compiled
        /// </summary>
        /// <returns></returns>
        protected override List<FileToCompile> GetFilesToCompileInStepZero() {
            // Full deployment? 
            if (ForceFullDeploy || PreviousDeployedFiles == null)
                return _proEnv.Deployer.GetFilteredList(_sourceFiles.Keys, 0)
                    .Where(path => path.TestAgainstListOfPatterns(_proEnv.FilesPatternCompilable))
                    .Select(path => new FileToCompile(path))
                    .ToNonNullList();

            // list of files that need to be recompiled simply because the source changed
            var filesToCompile = new HashSet<string>(_sourceFilesNew, StringComparer.CurrentCultureIgnoreCase);

            // list the files that need to be recompiled because of table CRC change
            // and the files that need to be recompiled because one of the file needed to compile it has changed
            FilesToCompileBecauseOfTableCrcChangesOrDependencesModification(ref filesToCompile);

            return _proEnv.Deployer.GetFilteredList(filesToCompile, 0)
                .Where(path => path.TestAgainstListOfPatterns(_proEnv.FilesPatternCompilable))
                .Select(s => new FileToCompile(s, GetSourceFileBaseInfo(s).Size))
                .ToNonNullList();
        }

        /// <summary>
        /// List all the files that should be deployed from the source directory
        /// </summary>
        protected override List<FileToDeploy> GetFilesToDeployInStepOne() {
            // Full deployment? 
            if (ForceFullDeploy || PreviousDeployedFiles == null)
                return _proEnv.Deployer.GetFilteredList(_sourceFiles.Keys, 1)
                    .SelectMany(file => _proEnv.Deployer.GetTransfersNeededForFile(file, 1))
                    .ToNonNullList();

            // list of files that need to be recompiled simply because the source changed
            return _proEnv.Deployer.GetFilteredList(_sourceFilesNew, 1)
                .SelectMany(file => _proEnv.Deployer.GetTransfersNeededForFile(file, 1))
                .ToNonNullList();
        }

        protected override List<FileToDeploy> GetFilesToDeployInStepTwoAndMore(int currentStep) {
            var list = base.GetFilesToDeployInStepTwoAndMore(currentStep);

            if (currentStep == 2) {
                // in this step, we add the files that need to be deleted
                // for each file missing, loop for each targets
                foreach (var fileDeployed in _sourceFilesMissing
                    .Where(path => _prevSourceFiles.ContainsKey(path))
                    .Select(path => _prevSourceFiles[path])
                    .Where(file => file is FileDeployed)
                    .Cast<FileDeployed>()) {
                    foreach (var target in fileDeployed.Targets) {
                        // we can't delete certain files (for instance, files deployed in a .cab)
                        var deletionType = DeployTransferRule.GetDeletetionType(target.DeployType);
                        if (deletionType == DeployType.None) {
                            continue;
                        }

                        list.Add(FileToDeploy.New(deletionType, fileDeployed.SourcePath, Path.GetDirectoryName(target.TargetPath), null)
                            .Set(fileDeployed.SourcePath, target.TargetPath));
                    }
                }
            }

            return list;
        }

        #endregion

        #region List files

        /// <summary>
        /// Creates a list of all the files in the source directory, gather info on each file
        /// </summary>
        protected bool ListAllFilesInSourceDir() {
            try {
                _isListing = true;

                // get the list of files in the source dir with their basic info
                float step = 0.5f;
                foreach (var file in Utils.EnumerateFiles(_proEnv.SourceDirectory, "*", _proEnv.ExploreRecursively ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)) {
                    _cancelSource.Token.ThrowIfCancellationRequested();
                    GetSourceFileBaseInfo(file);
                    if ((ComputeMd5 ? 30 : 90) - _listingPercentage < 10 * step) {
                        step /= 10;
                    }
                    _listingPercentage += step;
                }

                // compute MD5 for each file (that's the step that takes the most time)
                if (ComputeMd5) {
                    var step1 = (90 - _listingPercentage) / _sourceFiles.Count;
                    ParallelOptions parallelOptions = new ParallelOptions {CancellationToken = _cancelSource.Token};
                    Parallel.ForEach(_sourceFiles, parallelOptions, pair => {
                        parallelOptions.CancellationToken.ThrowIfCancellationRequested();
                        try {
                            using (var md5 = MD5.Create()) {
                                using (var stream = File.OpenRead(pair.Value.SourcePath)) {
                                    pair.Value.Md5 = md5.ComputeHash(stream);
                                }
                            }
                        } catch (Exception e) {
                            ErrorHandler.LogErrors(e, "Impossible d'obtenir le MD5 du fichier " + pair.Value.SourcePath.Quoter());
                        }
                        _listingPercentage += step1;
                    });
                }

                // now list all the files that are new (either they didn't exist in the previous deployment or they changed since)
                step = (95 - _listingPercentage) / _sourceFiles.Count;
                foreach (var file in _sourceFiles) {
                    _listingPercentage += step;
                    _cancelSource.Token.ThrowIfCancellationRequested();
                    var source = file.Key;
                    // the file existed before
                    if (_prevSourceFiles.ContainsKey(source)) {
                        var prevFile = _prevSourceFiles[source];

                        // test if it has changed
                        if (prevFile.Size.Equals(file.Value.Size)) {
                            // if it didn't change, we don't need to compute it again
                            if (prevFile.LastWriteTime.Equals(file.Value.LastWriteTime) ||
                                ComputeMd5 && prevFile.Md5 != null && file.Value.Md5 != null && prevFile.Md5.SequenceEqual(file.Value.Md5)) {
                                if (!_sourceFilesUpToDate.Contains(file.Key))
                                    _sourceFilesUpToDate.Add(file.Key);
                                continue;
                            }
                        }
                    }
                    if (!_sourceFilesNew.Contains(file.Key))
                        _sourceFilesNew.Add(file.Key);
                }

                // list all the files that are missing from the previous deployment
                step = (100 - _listingPercentage) / _sourceFiles.Count;
                foreach (var prevFilePath in _prevSourceFiles.Keys) {
                    if (!_sourceFiles.ContainsKey(prevFilePath) && !_sourceFilesMissing.Contains(prevFilePath)) {
                        _sourceFilesMissing.Add(prevFilePath);
                    }
                    _listingPercentage += step;
                }

                _listingPercentage = 100;
                _isListing = false;

                return true;
            } catch (OperationCanceledException) {
                // we expect this exception if the task has been canceled
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Une erreur est survenue lors du listing des fichiers source");
            }
            return false;
        }

        /// <summary>
        /// list the files that need to be recompiled because of table CRC change OR
        /// because one of the file needed to compile it has changed
        /// We only take into account include files, we don't consider that a file that has dependences (i.e. a *.p needs a *.i)
        /// can be a dependence of another file
        /// </summary>
        private void FilesToCompileBecauseOfTableCrcChangesOrDependencesModification(ref HashSet<string> filesToCompile) {
            // we get a list of the current DB.TABLE + the CRC for each
            List<TableCrc> currentTables = new List<TableCrc>();

            if (!string.IsNullOrEmpty(_proEnv.ConnectionString)) {
                var exec = new ProExecutionTableCrc {
                    NeedDatabaseConnection = true
                };
                exec.Start();
                exec.WaitForProcessExit(0);
                currentTables = exec.GetTableCrc();
                if (currentTables == null) {
                    return;
                }
            }

            // for each previously compiled file that required a table
            foreach (var prevCompiledFile in PreviousDeployedFiles
                .Where(file => file is FileDeployedCompiled)
                .Cast<FileDeployedCompiled>()) {
                // if the file is not part of the sources anymore, no need to recompile obviously
                if (!_sourceFiles.ContainsKey(prevCompiledFile.SourcePath)) {
                    continue;
                }
                // we already added this file to be recompiled, continue
                if (filesToCompile.Contains(prevCompiledFile.SourcePath)) {
                    continue;
                }
                // for each required table of said file
                if (prevCompiledFile.RequiredTables != null) {
                    foreach (var tableRequired in prevCompiledFile.RequiredTables) {
                        var currentTable = currentTables.Find(table => table.QualifiedTableName.EqualsCi(tableRequired.QualifiedTableName));

                        // the file uses a now unknown table or uses a table for which the CRC changed?
                        if (currentTable == null || !currentTable.Crc.Equals(tableRequired.Crc)) {
                            // need to recompile the file
                            filesToCompile.Add(prevCompiledFile.SourcePath);
                        }
                    }
                }
                // for each required file
                if (prevCompiledFile.RequiredFiles != null) {
                    foreach (var requiredFile in prevCompiledFile.RequiredFiles.Where(file => file != null)) {
                        // one of the required file has changed
                        if (_sourceFilesNew.Contains(requiredFile.SourcePath)) {
                            // need to recompile the file
                            filesToCompile.Add(prevCompiledFile.SourcePath);
                        }
                    }
                }
            }
        }

        #endregion

        #region Utils

        /// <summary>
        /// Returns the basic info for a given file path (in the source dir)
        /// </summary>
        private FileSourceInfo GetSourceFileBaseInfo(string sourcePath) {
            try {
                if (_sourceFiles.ContainsKey(sourcePath)) {
                    return _sourceFiles[sourcePath];
                }
                if (!File.Exists(sourcePath)) {
                    return null;
                }
                var fileInfo = new FileInfo(sourcePath);
                var newInfo = new FileSourceInfo {
                    SourcePath = sourcePath,
                    Size = fileInfo.Length,
                    LastWriteTime = fileInfo.LastWriteTime
                };
                _sourceFiles.Add(sourcePath, newInfo);
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Impossible d'obtenir des informations sur le fichier " + sourcePath.Quoter());
            }
            return null;
        }

        /// <summary>
        /// Convert each path of each object to a relative path
        /// </summary>
        private List<FileDeployed> ConvertToRelativePath(List<FileDeployed> list) {
            foreach (var file in list) {
                try {
                    file.SourcePath = file.SourcePath.Replace(_proEnv.SourceDirectory.CorrectDirPath(), "");
                    if (file.Targets != null) {
                        foreach (var target in file.Targets) {
                            target.TargetPath = target.TargetPath.Replace(_proEnv.TargetDirectory.CorrectDirPath(), "");
                            if (!string.IsNullOrEmpty(target.TargetPackPath))
                                target.TargetPackPath = target.TargetPackPath.Replace(_proEnv.TargetDirectory.CorrectDirPath(), "");
                        }
                    }
                    var compFile = file as FileDeployedCompiled;
                    if (compFile != null && compFile.RequiredFiles != null) {
                        foreach (var fileRequired in compFile.RequiredFiles) {
                            fileRequired.SourcePath = fileRequired.SourcePath.Replace(_proEnv.SourceDirectory.CorrectDirPath(), "");
                        }
                    }
                } catch (Exception e) {
                    ErrorHandler.LogErrors(e);
                }
            }
            return list;
        }

        /// <summary>
        /// Convert each path of each object to an absolute path
        /// </summary>
        private List<FileDeployed> ConvertToAbsolutePath(List<FileDeployed> list) {
            // we make each path absolute
            foreach (var file in list) {
                try {
                    file.SourcePath = Path.Combine(_proEnv.SourceDirectory, file.SourcePath);
                    if (file.Targets != null) {
                        foreach (var target in file.Targets) {
                            target.TargetPath = Path.Combine(_proEnv.TargetDirectory, target.TargetPath);
                            if (!string.IsNullOrEmpty(target.TargetPackPath))
                                target.TargetPackPath = Path.Combine(_proEnv.TargetDirectory, target.TargetPackPath);
                        }
                    }
                    var compFile = file as FileDeployedCompiled;
                    if (compFile != null && compFile.RequiredFiles != null) {
                        foreach (var fileRequired in compFile.RequiredFiles) {
                            fileRequired.SourcePath = Path.Combine(_proEnv.SourceDirectory, fileRequired.SourcePath);
                        }
                    }
                } catch (Exception e) {
                    ErrorHandler.LogErrors(e);
                }
            }
            return list;
        }

        #endregion

        #region FormatDeploymentResults

        /// <summary>
        /// Generate an html report for the current deployment
        /// </summary>
        public override string FormatDeploymentParameters() {
            return base.FormatDeploymentParameters() + @"
                <div class='IndentDiv'>
                    <div>Déploiement FULL : <b>" + _proEnv.ForceFullDeploy + @"</b></div>
                    <div>Calcul du MD5 des fichiers : <b>" + _proEnv.ComputeMd5 + @"</b></div> 
                </div>";
        }

        /// <summary>
        /// Generate an html report for the current deployment
        /// </summary>
        public override string FormatDeploymentResults() {
            var sb = new StringBuilder(base.FormatDeploymentResults());
            sb.AppendLine(@"             
                <h2>Informations complémentaires sur le listing :</h2>
                <div class='IndentDiv'>");
            var deployedFiles = DeployedFilesOutput.Select(deployed => deployed.SourcePath).ToList();
            deployedFiles.AddRange(DeployedFilesOutput.Where(deployed => deployed is FileDeployedCompiled).Cast<FileDeployedCompiled>().SelectMany(deployed => deployed.RequiredFiles).Select(info => info.SourcePath));
            var sourceFilesNotDeployed = _sourceFiles.Select(pair => pair.Key.Replace(_proEnv.SourceDirectory.CorrectDirPath(), "")).Where(sourceFilePath => !deployedFiles.Exists(path => path.Equals(sourceFilePath))).ToList();
            sb.AppendLine(@"             
                    <div>Nombre de fichiers non déployés trouvés (avant action) : <b>" + _sourceFilesNew.Count + @"</b></div>
                    <div>Nombre de fichiers identiques au dernier déploiement : <b>" + _sourceFilesUpToDate.Count + @"</b></div> 
                    <div>Nombre de fichiers manquants par rapport au dernier déploiement : <b>" + _sourceFilesMissing.Count + @"</b></div>
                    <div>Nombre de fichiers source non utilisés pour ce déploiement : <b>" + sourceFilesNotDeployed.Count + @"</b></div>
            ");
            if (sourceFilesNotDeployed.Count > 0) {
                sb.AppendLine(@"<h3>Liste des fichiers non utilisés pour ce déploiement</h3>");
                sb.AppendLine(@"<div class='IndentDiv'>");
                foreach (var undeployedFile in sourceFilesNotDeployed) {
                    sb.AppendLine(@"<div><img height='15px' src='" + Utils.GetExtensionImage((Path.GetExtension(undeployedFile) ?? "").Replace(".", ""), false) + "'>" + Path.Combine(_proEnv.SourceDirectory, undeployedFile).ToHtmlLink(undeployedFile, true) + @"</div>");
                }
                sb.AppendLine(@"</div>");
            }
            sb.AppendLine(@"</div>");
            return sb.ToString();
        }

        #endregion
    }
}