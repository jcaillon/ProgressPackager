#region header

// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (DeploymentHandlerPackaging.cs) is part of csdeployer.
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
using abldeployer.Compression.Prolib;
using abldeployer.Core.Exceptions;
using abldeployer.Lib;

namespace abldeployer.Core {
    /// <summary>
    ///     This type of deployment adds a layer that creates the webclient folder :
    ///     - copy reference n-1 (ReferenceDirectory) to reference n (TargetDirectory)
    ///     - normal comp/deploy step 0/1
    ///     - normal deploy step 2
    ///     - folder clientNWP -> AAA.cab
    ///     - build /diffs/
    /// </summary>
    public class DeploymentHandlerPackaging : DeploymentHandlerDifferential {
        #region Life and death

        /// <summary>
        ///     Constructor
        /// </summary>
        /// <param name="proEnv"></param>
        public DeploymentHandlerPackaging(Config.ProConfig proEnv) : base(proEnv) {
            if (proEnv.CreatePackageInTempDir) {
                // overload the target directory, we copy it in the real target at the end
                proEnv.InitialTargetDirectory = proEnv.TargetDirectory;
                proEnv.TargetDirectory = Path.Combine(proEnv.FolderTemp, "Package");
            }
        }

        #endregion

        #region utils

        /// <summary>
        ///     We might have used a temporary folder as a target dir
        ///     we want to set the real target dir so it appears correct in the report
        /// </summary>
        private void CorrectTargetDirectory() {
            // we correct the target directory so it appears correct to the user
            try {
                foreach (var filesToDeploy in _filesToDeployPerStep.Values)
                    foreach (var file in filesToDeploy) {
                        file.To = file.To.Replace(_proEnv.TargetDirectory.CorrectDirPath(), _proEnv.InitialTargetDirectory.CorrectDirPath());
                        file.TargetBasePath = file.TargetBasePath.Replace(_proEnv.TargetDirectory.CorrectDirPath(), _proEnv.InitialTargetDirectory.CorrectDirPath());
                        var fileToPack = file as FileToDeployInPack;
                        if (fileToPack != null)
                            fileToPack.PackPath = fileToPack.PackPath.Replace(_proEnv.TargetDirectory.CorrectDirPath(), _proEnv.InitialTargetDirectory.CorrectDirPath());
                    }

                foreach (var diffCab in _diffCabs)
                    diffCab.CabPath = diffCab.CabPath.Replace(_proEnv.TargetDirectory.CorrectDirPath(), _proEnv.InitialTargetDirectory.CorrectDirPath());
            } catch (Exception e) {
                AddHandledExceptions(e);
            }

            _proEnv.TargetDirectory = _proEnv.InitialTargetDirectory;
        }

        #endregion

        #region DiffCab

        public class DiffCab {
            /// <summary>
            ///     1
            /// </summary>
            public int VersionToUpdateFrom { get; set; }

            /// <summary>
            ///     2
            /// </summary>
            public int VersionToUpdateTo { get; set; }

            /// <summary>
            ///     $TARGET/wcp/new-10-02/diffs/new1to2.cab
            /// </summary>
            public string CabPath { get; set; }

            /// <summary>
            ///     $REFERENCE/wcp/new-10-02/diffs/new1to2.cab
            /// </summary>
            public string ReferenceCabPath { get; set; }

            /// <summary>
            ///     $TARGET/wcp/new-10-02/diffs/new1to2
            /// </summary>
            public string TempCabFolder { get; set; }

            /// <summary>
            ///     List of all the files that were deployed in the clientNWK since this VersionToUpdateFrom
            /// </summary>
            public List<FileDeployed> FilesDeployedInNwkSincePreviousVersion { get; set; }

            /// <summary>
            ///     List of all the files to pack (for step 1, which is we move/cab files into a temporary directory for this diff)
            /// </summary>
            public List<FileToDeploy> FilesToPackStep1 { get; set; }
        }

        #endregion

        #region Properties

        /// <summary>
        ///     We add 3 operations
        /// </summary>
        public override int TotalNumberOfOperations {
            get { return base.TotalNumberOfOperations + (CreateWebClient ? 3 : 1) + (_proEnv.CreatePackageInTempDir ? 1 : 0); }
        }

        /// <summary>
        ///     add the listing operation
        /// </summary>
        public override float OverallProgressionPercentage {
            get { return base.OverallProgressionPercentage + _refCopyPercentage + _buildDiffPercentage / 2 + _buildAppCabPercentage / 2 + _finalPackageCopyPercentage; }
        }

        /// <summary>
        ///     Returns the name of the current step
        /// </summary>
        public override DeploymentStep CurrentOperationName {
            get {
                if (_isCopyingFinalPackage)
                    return DeploymentStep.CopyingFinalPackageToDistant;
                if (_isCopyingRef)
                    return DeploymentStep.CopyingReference;
                if (_isBuildingDiffs)
                    return DeploymentStep.BuildingWebclientDiffs;
                if (_isBuildingAppCab)
                    return DeploymentStep.BuildingWebclientCompleteCab;
                return base.CurrentOperationName;
            }
        }

        /// <summary>
        ///     Returns the progression for the current step
        /// </summary>
        public override float CurrentOperationPercentage {
            get {
                if (_isCopyingFinalPackage)
                    return _finalPackageCurrentCopyPercentage;
                if (_isCopyingRef)
                    return _refCopyPercentage;
                if (_isBuildingDiffs)
                    return _buildDiffPercentage / 2;
                if (_isBuildingAppCab)
                    return _buildAppCabPercentage / 2;
                return base.CurrentOperationPercentage;
            }
        }

        /// <summary>
        ///     List of the list of all previous deployments
        ///     sorted from oldest (0) to newer (n)
        /// </summary>
        public List<Config.ProConfig> ListPreviousDeployements { get; set; }

        /// <summary>
        ///     False if we must not create the webclient folder with all the .cab and prowcapp
        /// </summary>
        public bool CreateWebClient {
            get { return !string.IsNullOrEmpty(_proEnv.ClientWcpDirectoryName); }
        }

        public string ReferenceDirectory { get; private set; }

        public List<DiffCab> DiffCabs {
            get { return _diffCabs; }
        }

        public bool WebClientCreated { get; private set; }

        #endregion

        #region Fields

        private bool _isCopyingRef;
        private float _refCopyPercentage;

        private bool _isCopyingFinalPackage;
        private float _finalPackageCopyPercentage;
        private float _finalPackageCurrentCopyPercentage;
        private ProgressCopy _finalCopy;

        private bool _isBuildingAppCab;
        private float _buildAppCabPercentage;

        private bool _isBuildingDiffs;
        private float _buildDiffPercentage;

        private List<DiffCab> _diffCabs = new List<DiffCab>();

        #endregion

        #region Override

        /// <summary>
        ///     Do stuff before starting the treatment
        /// </summary>
        /// <returns></returns>
        /// <exception cref="DeploymentException"></exception>
        protected override void BeforeStarting() {
            if (string.IsNullOrEmpty(_proEnv.ReferenceDirectory)) {
                // if not defined, it is the target directory of the latest deployment
                if (ListPreviousDeployements != null && ListPreviousDeployements.Count > 0)
                    ReferenceDirectory = ListPreviousDeployements.Last().TargetDirectory;
            } else {
                ReferenceDirectory = _proEnv.ReferenceDirectory;
            }

            if (string.IsNullOrEmpty(_proEnv.TargetDirectory))
                throw new DeploymentException("The target directory is not set");

            if (string.IsNullOrEmpty(_proEnv.WcApplicationName))
                throw new DeploymentException("The webclient application name is not set");

            if (string.IsNullOrEmpty(_proEnv.WcPackageName))
                throw new DeploymentException("The webclient package name is not set");

            // first webclient package or new one?
            if (ListPreviousDeployements == null || ListPreviousDeployements.Count == 0)
                _proEnv.WcProwcappVersion = 1;
            else
                _proEnv.WcProwcappVersion = ListPreviousDeployements.Last().WcProwcappVersion + 1;

            // copy reference folder to target folder
            if (!IsTestMode)
                CopyReferenceDirectory();

            // creates a list of all the files in the source directory, gather info on each file
            ListAllFilesInSourceDir();
        }

        /// <summary>
        ///     Deployment for the step 1 and >=
        ///     OVERRIDE : we build the webclient folder just after the deployment step 2 (in which we might want to delete stuff
        ///     in the client nwk)
        /// </summary>
        protected override void DeployStepOneAndMore(int currentStep) {
            if (currentStep == 3 && !HasBeenCancelled && CreateWebClient && Directory.Exists(Path.Combine(_proEnv.TargetDirectory, _proEnv.ClientNwkDirectoryName))) {
                // First, we build the list of diffs that will need to be created, this allows us to know if there are actually differences
                // in the packaging for the webclient
                try {
                    BuildDiffsList();
                } catch (Exception e) {
                    AddHandledExceptions(e, "An error has occurred while computing the list of webclient diff cab files");
                    _deploymentErrorOccured = true;
                }
                // at this point we know each diff cab and the files that should compose it

                // do we need to build a new version of the webclient? did at least 1 file changed?
                if (!IsTestMode && (_diffCabs.Count == 0 || _diffCabs.First().FilesDeployedInNwkSincePreviousVersion.Count > 0)) {
                    // in this step, we add what's needed to build the .cab that has all the client files (the "from scratch" webclient install)
                    try {
                        _isBuildingAppCab = true;
                        BuildCompleteWebClientCab();
                        _isBuildingAppCab = false;
                    } catch (Exception e) {
                        AddHandledExceptions(e, "An error has occurred while creating the complete webclient cab file");
                        _deploymentErrorOccured = true;
                    }

                    // at this step, we need to build the /diffs/ for the webclient
                    try {
                        _isBuildingDiffs = true;
                        BuildDiffsWebClientCab();
                        _isBuildingDiffs = false;
                    } catch (Exception e) {
                        AddHandledExceptions(e, "An error has occurred while creating the webclient diff cab files");
                        _deploymentErrorOccured = true;
                    }

                    // finally, build the prowcapp file for the version
                    try {
                        BuildCompleteProwcappFile();
                    } catch (Exception e) {
                        AddHandledExceptions(e, "An error has occurred while creating the webclient prowcapp file");
                        _deploymentErrorOccured = true;
                    }

                    WebClientCreated = true;

                    // check that every webclient file we should have created actually exist
                    var fullCabPath = Path.Combine(_proEnv.TargetDirectory, _proEnv.ClientWcpDirectoryName, _proEnv.WcApplicationName + ".cab");
                    if (!File.Exists(fullCabPath))
                        _deploymentErrorOccured = true;
                    foreach (var cab in _diffCabs)
                        if (!File.Exists(cab.CabPath))
                            _deploymentErrorOccured = true;
                } else if (ListPreviousDeployements != null && ListPreviousDeployements.Count > 0) {
                    // copy the previous webclient folder in this deployment
                    try {
                        CopyPreviousWebclientFolder();

                        // set the webclient package number accordingly
                        _proEnv.WcProwcappVersion = ListPreviousDeployements.Last().WcProwcappVersion;
                        _proEnv.WcPackageName = ListPreviousDeployements.Last().WcPackageName;
                    } catch (Exception e) {
                        AddHandledExceptions(e, "Erreur durant la copie du dossier webclient précédent");
                        _deploymentErrorOccured = true;
                    }
                }
            }

            base.DeployStepOneAndMore(currentStep);
        }

        #region DoExecutionOk

        /// <summary>
        ///     Called after the deployment, we might need to copy the package created locally to the remote directory
        /// </summary>
        protected override void BeforeEndOfSuccessfulDeployment() {
            // if we created the package in a temp directory
            if (_proEnv.CreatePackageInTempDir)
                if (_proEnv.IsTestMode) {
                    CorrectTargetDirectory();
                } else {
                    _isCopyingFinalPackage = true;

                    // copy the local package to the remote location (the real target dir)
                    if (Directory.Exists(_proEnv.TargetDirectory)) {
                        _finalCopy = ProgressCopy.CopyDirectory(_proEnv.TargetDirectory, _proEnv.InitialTargetDirectory, CpOnCompleted, CpOnProgressChanged);
                        return;
                    }

                    Utils.CreateDirectory(_proEnv.InitialTargetDirectory);
                }

            EndOfDeployment();
        }

        private void CpOnProgressChanged(object sender, ProgressCopy.ProgressEventArgs progressArgs) {
            _finalPackageCurrentCopyPercentage = (float) progressArgs.CurrentFile;
            _finalPackageCopyPercentage = (float) progressArgs.TotalFiles;
        }

        private void CpOnCompleted(object sender, ProgressCopy.EndEventArgs endEventArgs) {
            if (endEventArgs.Exception != null)
                AddHandledExceptions(endEventArgs.Exception);
            _isCopyingFinalPackage = false;
            CorrectTargetDirectory();
            if (endEventArgs.Type == ProgressCopy.CopyCompletedType.Aborted) {
                base.Cancel();
                return;
            }

            if (endEventArgs.Type == ProgressCopy.CopyCompletedType.Exception)
                _deploymentErrorOccured = true;
            EndOfDeployment();
        }

        public override void Cancel() {
            if (_finalCopy != null)
                _finalCopy.AbortCopyAsync();
            base.Cancel();
        }

        #endregion

        #endregion

        #region Private

        /// <summary>
        ///     copy reference folder to target folder
        /// </summary>
        /// <returns></returns>
        /// <exception cref="DeploymentException"></exception>
        private bool CopyReferenceDirectory() {
            if (Directory.Exists(_proEnv.TargetDirectory))
                try {
                    Directory.Delete(_proEnv.TargetDirectory, true);
                } catch (Exception e) {
                    throw new DeploymentException("Couldn't clean the target directory : " + _proEnv.TargetDirectory.Quoter(), e);
                }

            if (Directory.Exists(_proEnv.InitialTargetDirectory))
                try {
                    Directory.Delete(_proEnv.InitialTargetDirectory, true);
                } catch (Exception e) {
                    throw new DeploymentException("Couldn't clean the target directory : " + _proEnv.InitialTargetDirectory.Quoter(), e);
                }

            // at this step, we need to copy the folder reference n-1 to reference n as well as set the target directory to reference n
            if (ListPreviousDeployements != null && ListPreviousDeployements.Count > 0 && Directory.Exists(ReferenceDirectory)) {
                _isCopyingRef = true;
                var filesToCopy = new List<FileToDeploy>();
                foreach (var file in Directory.EnumerateFiles(ReferenceDirectory, "*", SearchOption.AllDirectories)) {
                    // do not copy the webclient folder
                    if (CreateWebClient && file.Contains(_proEnv.ClientWcpDirectoryName.CorrectDirPath()))
                        continue;
                    var targetPath = Path.Combine(_proEnv.TargetDirectory, file.Replace(ReferenceDirectory.CorrectDirPath(), ""));
                    filesToCopy.Add(new FileToDeployCopy(file, Path.GetDirectoryName(targetPath), null).Set(file, targetPath));
                }

                // copy files
                if (IsTestMode)
                    filesToCopy.ForEach(deploy => deploy.IsOk = true);
                else
                    _proEnv.Deployer.DeployFiles(filesToCopy, f => _refCopyPercentage = f, _cancelSource);
                _refCopyPercentage = 100;
                // display the errors in the report
                _filesToDeployPerStep.Add(-1, filesToCopy.Where(deploy => !deploy.IsOk).ToList());
                _isCopyingRef = false;
                if (filesToCopy.Exists(deploy => !deploy.IsOk))
                    throw new DeploymentException("At least one error has occurred while copying the reference directory : " + filesToCopy.First(deploy => !deploy.IsOk).DeployError);
            }

            return true;
        }

        /// <summary>
        ///     Copies the webclient folder of the reference in the deployment
        /// </summary>
        private void CopyPreviousWebclientFolder() {
            var prevWcpDir = Path.Combine(ReferenceDirectory, _proEnv.ClientWcpDirectoryName);
            if (Directory.Exists(prevWcpDir)) {
                _isBuildingAppCab = true;
                var filesToCopy = new List<FileToDeploy>();
                foreach (var file in Directory.EnumerateFiles(prevWcpDir, "*", SearchOption.AllDirectories)) {
                    var targetPath = Path.Combine(Path.Combine(_proEnv.TargetDirectory, _proEnv.ClientWcpDirectoryName), file.Replace(prevWcpDir.CorrectDirPath(), ""));
                    filesToCopy.Add(new FileToDeployCopy(file, Path.GetDirectoryName(targetPath), null).Set(file, targetPath));
                }

                // copy files
                if (IsTestMode)
                    filesToCopy.ForEach(deploy => deploy.IsOk = true);
                else
                    _proEnv.Deployer.DeployFiles(filesToCopy, f => _buildAppCabPercentage = f, _cancelSource);
                _buildAppCabPercentage = 100;
                // display the errors in the report
                _filesToDeployPerStep.Add(MaxStep + 1, filesToCopy.Where(deploy => !deploy.IsOk).ToList());
                _isBuildingAppCab = false;
            }
        }

        /// <summary>
        ///     Copies the content of the folder client NWK in the complete AAA.cab
        /// </summary>
        private void BuildCompleteWebClientCab() {
            // build the list of files to deploy
            var filesToCab = ListFilesInDirectoryToDeployInCab(Path.Combine(_proEnv.TargetDirectory, _proEnv.ClientNwkDirectoryName), Path.Combine(_proEnv.TargetDirectory, _proEnv.ClientWcpDirectoryName, _proEnv.WcApplicationName + ".cab"));

            // actually deploy them
            if (IsTestMode)
                filesToCab.ForEach(deploy => deploy.IsOk = true);
            else
                _proEnv.Deployer.DeployFiles(filesToCab, f => _buildAppCabPercentage = 100 + f, _cancelSource);
            _buildAppCabPercentage = 200;
            _filesToDeployPerStep.Add(MaxStep + 1, filesToCab.Where(deploy => !deploy.IsOk).ToList());
        }

        /// <summary>
        ///     Build the list of diffs .cab,
        ///     At the end we know each diff cab that need to be created and all the files that should compose each of the diff
        /// </summary>
        private void BuildDiffsList() {
            // here we build the list of each /diffs/ .cab files to create for the webclient
            for (int i = ListPreviousDeployements.Count - 1; i >= 0; i--) {
                var deployment = ListPreviousDeployements[i];
                List<FileDeployed> deployedFileInPreviousVersion;
                Config.ProConfig nextDeployment;
                List<FileDeployed> deployedFileInNextVersion;

                // list of the files that are deployed in this version + 1
                if (i + 1 < ListPreviousDeployements.Count) {
                    nextDeployment = ListPreviousDeployements[i + 1];
                    deployedFileInNextVersion = nextDeployment.DeployedFiles;
                    deployedFileInPreviousVersion = deployment.DeployedFiles;
                } else {
                    nextDeployment = _proEnv;
                    deployedFileInNextVersion = DeployedFilesOutput;
                    deployedFileInPreviousVersion = PreviousDeployedFiles;
                }

                // add all the files added/deleted/replaced in the client networking directory in this version + 1
                var filesDeployedInNextVersion = new List<FileDeployed>();
                if (deployedFileInNextVersion != null) {
                    foreach (var file in deployedFileInNextVersion.Where(deployed => deployed.Action != DeploymentAction.Existing && deployed.Targets.Exists(target => target.TargetPath.ContainsFast(_proEnv.ClientNwkDirectoryName.CorrectDirPath())))) {
                        filesDeployedInNextVersion.Add(new FileDeployed {
                            SourcePath = file.SourcePath,
                            Action = file.Action,
                            Targets = file.Targets.Where(target => target.TargetPath.ContainsFast(_proEnv.ClientNwkDirectoryName.CorrectDirPath())).ToList()
                        });
                    }

                    // the below treatment is a way to handle the following webclient mecanism :
                    // the webclient updates an existing pl with .r files stored in a .cab (named like the .pl) within the diff.cab
                    // however, if the .pl does not exist before the update, the system is different, we simply store
                    // the .pl in the diff.cab and we say we "add" this .pl
                    // What we are doing below is : if it's a full deployment or if a new .pl is created, we add the .pl to be
                    // added in the diff.cab as a simple file and we remove all the single files that compose this .pl
                    var listPl = new Dictionary<string, bool>(); // pl path -> has at least 1 new file added
                    foreach (var fileDeployed in filesDeployedInNextVersion) {
                        foreach (var target in fileDeployed.Targets.Where(target => target.DeployType == DeployType.Prolib || target.DeployType == DeployType.DeleteInProlib)) {
                            if (!listPl.ContainsKey(target.TargetPackPath))
                                listPl.Add(target.TargetPackPath, false);
                            if (fileDeployed.Action == DeploymentAction.Added)
                                listPl[target.TargetPackPath] = true;
                        }
                    }

                    foreach (var kpv in listPl) {
                        // case of a full deployment or if the .pl did not exist in the previous deployment, we ADD the .pl
                        if (nextDeployment.ForceFullDeploy || listPl[kpv.Key] && (deployedFileInPreviousVersion == null || !deployedFileInPreviousVersion.Exists(deployed => deployed.Targets.Exists(target => !string.IsNullOrEmpty(target.TargetPackPath) && target.TargetPackPath.Equals(kpv.Key, StringComparison.CurrentCultureIgnoreCase))))) {
                            // we add the .pl so it is added to the diff .cab
                            filesDeployedInNextVersion.Add(new FileDeployed {
                                SourcePath = kpv.Key,
                                Action = DeploymentAction.Added,
                                Targets = new List<DeploymentTarget> {
                                    new DeploymentTarget {
                                        DeployType = DeployType.Copy,
                                        TargetPath = kpv.Key
                                    }
                                }
                            });

                            // we remove all the files deployed in this .pl so they do not directly appear in the diff .cab (they already are in the pl)
                            foreach (var fileDeployed in filesDeployedInNextVersion.Where(deployed => deployed.Targets != null)) {
                                fileDeployed.Targets.RemoveAll(target => !string.IsNullOrEmpty(target.TargetPackPath) && target.TargetPackPath.Equals(kpv.Key, StringComparison.CurrentCultureIgnoreCase));
                            }

                            filesDeployedInNextVersion.RemoveAll(deployed => deployed.Targets != null && deployed.Targets.Count == 0);
                        }
                    }
                }

                // the 2 versions can be equal if we did a package where we had no diff for the webclient so we just copied the previous webclient folder
                if (deployment.WcProwcappVersion != nextDeployment.WcProwcappVersion) {
                    _diffCabs.Add(new DiffCab {
                        VersionToUpdateFrom = deployment.WcProwcappVersion,
                        VersionToUpdateTo = nextDeployment.WcProwcappVersion,
                        CabPath = Path.Combine(_proEnv.TargetDirectory, _proEnv.ClientWcpDirectoryName, "diffs", _proEnv.WcApplicationName + deployment.WcProwcappVersion + "to" + nextDeployment.WcProwcappVersion + ".cab"),
                        ReferenceCabPath = Path.Combine(ReferenceDirectory ?? "", _proEnv.ClientWcpDirectoryName, "diffs", _proEnv.WcApplicationName + deployment.WcProwcappVersion + "to" + nextDeployment.WcProwcappVersion + ".cab"),
                        FilesDeployedInNwkSincePreviousVersion = filesDeployedInNextVersion.ToList()
                    });
                }
            }
        }

        /// <summary>
        ///     Actually build the diffs .cab :
        ///     We have to do it in 2 steps because the files in a pl must be added to a .cab
        ///     - step 1 : we prepare a temp folder for each diff were we copy the file or add them in .cab if needed (if they are
        ///     from a .pl)
        ///     - step 2 : create the .wcm for each diff in each temp folder
        ///     - step 3 : we .cab this temp folder into the final diff.cab
        ///     Note : for older diffs, we don't actually recreate them, they should already exist in the reference directory so we
        ///     only copy them
        ///     if they don't exist however, we can recreate them here
        /// </summary>
        private void BuildDiffsWebClientCab() {
            var tempFolderPath = Path.Combine(_proEnv.TargetDirectory, _proEnv.ClientWcpDirectoryName, Path.GetRandomFileName());
            try {
                // certain diff cab must be created, others already exist in the previous deployment /diffs/ folder (for them we only copy the existing .cab)
                var diffCabTodo = new List<DiffCab>();
                foreach (var diffCab in _diffCabs)
                    if (File.Exists(diffCab.ReferenceCabPath))
                        diffCab.FilesToPackStep1 = new List<FileToDeploy> {
                            new FileToDeployCopy(diffCab.ReferenceCabPath, Path.GetDirectoryName(diffCab.CabPath), null).Set(diffCab.ReferenceCabPath, diffCab.CabPath)
                        };
                    else
                        diffCabTodo.Add(diffCab);

                // pl relative path -> targets
                var plNeedingExtraction = new Dictionary<string, List<DeploymentTarget>>(StringComparer.CurrentCultureIgnoreCase);

                // we list all the files needed to be deployed for each diff cab
                foreach (var diffCab in diffCabTodo) {
                    // path to the .cab to create
                    diffCab.TempCabFolder = diffCab.CabPath.Replace(".cab", "");
                    diffCab.FilesToPackStep1 = new List<FileToDeploy>();

                    foreach (var target in diffCab.FilesDeployedInNwkSincePreviousVersion.Where(deployed => deployed.Action != DeploymentAction.Deleted).SelectMany(deployed => deployed.Targets)) {
                        var from = target.TargetPath;
                        var to = Path.Combine(diffCab.TempCabFolder, target.TargetPath.Replace(_proEnv.ClientNwkDirectoryName.CorrectDirPath(), ""));

                        if (target.DeployType == DeployType.Prolib) {
                            to = to.Replace(".pl\\", ".cab\\");
                            diffCab.FilesToPackStep1.Add(new FileToDeployCab(from, Path.GetDirectoryName(to), null).Set(from, to));
                            if (!string.IsNullOrEmpty(target.TargetPackPath)) {
                                if (!plNeedingExtraction.ContainsKey(target.TargetPackPath))
                                    plNeedingExtraction.Add(target.TargetPackPath, new List<DeploymentTarget>());
                                if (!plNeedingExtraction[target.TargetPackPath].Exists(deploymentTarget => deploymentTarget.TargetPath.Equals(target.TargetPath)))
                                    plNeedingExtraction[target.TargetPackPath].Add(target);
                            }
                        } else {
                            diffCab.FilesToPackStep1.Add(new FileToDeployCopy(from, Path.GetDirectoryName(to), null).Set(from, to));
                        }
                    }
                }

                // we have the list of all the files that we need to extract from a .pl, we extract them into a temp directory for each pl
                var extractedFilesFromPl = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
                foreach (var kpv in plNeedingExtraction) {
                    var plExtractionFolder = Path.Combine(tempFolderPath, kpv.Key.Replace(_proEnv.ClientNwkDirectoryName.CorrectDirPath(), "") + Path.GetRandomFileName());
                    var extractor = new ProlibExtractor(Path.Combine(_proEnv.TargetDirectory, kpv.Key), _proEnv.ProlibPath, plExtractionFolder);
                    extractor.ExtractFiles(kpv.Value.Select(target => target.TargetPathInPack).ToList());
                    foreach (var target in kpv.Value)
                        if (!extractedFilesFromPl.ContainsKey(target.TargetPath))
                            extractedFilesFromPl.Add(target.TargetPath, Path.Combine(plExtractionFolder, target.TargetPathInPack));
                }

                // now we correct the FROM property for each file to deploy, so that it can copy correctly from an existing file
                foreach (var diffCab in diffCabTodo)
                    foreach (var fileToDeploy in diffCab.FilesToPackStep1)
                        if (fileToDeploy is FileToDeployCab) {
                            if (extractedFilesFromPl.ContainsKey(fileToDeploy.From))
                                fileToDeploy.From = extractedFilesFromPl[fileToDeploy.From];
                        } else {
                            fileToDeploy.From = Path.Combine(_proEnv.TargetDirectory, fileToDeploy.From);
                        }

                // STEP 1 : we move the files into a temporary folder (one for each diff) (or/and copy existing .cab from the reference/diffs/)
                if (IsTestMode)
                    _diffCabs.ForEach(cab => cab.FilesToPackStep1.ForEach(deploy => deploy.IsOk = true));
                else
                    _proEnv.Deployer.DeployFiles(_diffCabs.SelectMany(cab => cab.FilesToPackStep1).ToList(), f => _buildDiffPercentage = f, _cancelSource);
                _buildDiffPercentage = 100;
                _filesToDeployPerStep.Add(MaxStep + 2, _diffCabs.SelectMany(cab => cab.FilesToPackStep1).ToList().Where(deploy => !deploy.IsOk).ToList());

                // STEP 2 : create each .wcm
                foreach (var diffCab in diffCabTodo)
                    BuildDiffWcmFile(diffCab);

                // STEP 3 : we need to convert each temporary folder into a .cab file
                // build the list of files to deploy
                var filesToCab = new List<FileToDeploy>();
                foreach (var diffCab in diffCabTodo)
                    if (Directory.Exists(diffCab.TempCabFolder))
                        filesToCab.AddRange(ListFilesInDirectoryToDeployInCab(diffCab.TempCabFolder, diffCab.CabPath));
                if (IsTestMode)
                    filesToCab.ForEach(deploy => deploy.IsOk = true);
                else
                    _proEnv.Deployer.DeployFiles(filesToCab, f => _buildDiffPercentage = 100 + f, _cancelSource);
                _buildDiffPercentage = 200;
                _filesToDeployPerStep[MaxStep + 2].AddRange(filesToCab.Where(deploy => !deploy.IsOk).ToList());

                // now we just need to clean the temporary folders for each diff
                foreach (var diffCab in diffCabTodo)
                    Utils.DeleteDirectory(diffCab.TempCabFolder, true);
            } finally {
                Utils.DeleteDirectory(tempFolderPath, true);
            }
        }

        /// <summary>
        ///     Build each .wcm
        /// </summary>
        private void BuildDiffWcmFile(DiffCab diffCab) {
            // for the diff, we build the corresponding wcm file
            var wcmPath = Path.Combine(diffCab.TempCabFolder, Path.ChangeExtension(Path.GetFileName(diffCab.CabPath ?? ""), "wcm"));
            Utils.CreateDirectory(diffCab.TempCabFolder);

            var mainSb = new StringBuilder();
            mainSb.AppendLine("[Main]");
            mainSb.AppendLine("FormatVersion=1");

            // changes not in .pl files
            mainSb.AppendLine("\r\n[Changes]");
            foreach (var fileDeployed in diffCab.FilesDeployedInNwkSincePreviousVersion)
                foreach (var target in fileDeployed.Targets.Where(target => target.DeployType != DeployType.Prolib && target.DeployType != DeployType.DeleteInProlib))
                    mainSb.AppendLine(string.Format("{0}={1}", target.TargetPath.Replace(_proEnv.ClientNwkDirectoryName.CorrectDirPath(), ""), GetActionLetter(fileDeployed.Action)));

            // changes in .pl files
            var plChanges = new Dictionary<string, string>(); // pl path -> list of changes in pl
            foreach (var fileDeployed in diffCab.FilesDeployedInNwkSincePreviousVersion)
                foreach (var target in fileDeployed.Targets.Where(target => target.DeployType == DeployType.Prolib || target.DeployType == DeployType.DeleteInProlib)) {
                    var relativePlPath = target.TargetPackPath.Replace(_proEnv.ClientNwkDirectoryName.CorrectDirPath(), "");
                    if (!plChanges.ContainsKey(relativePlPath))
                        plChanges.Add(relativePlPath, "");
                    plChanges[relativePlPath] += string.Format("{0}={1}\r\n", target.TargetPathInPack, GetActionLetter(fileDeployed.Action));
                }

            if (plChanges.Count > 0) {
                mainSb.AppendLine("\r\n[PLFiles]");
                foreach (var kpv in plChanges) // list all modified pl files
                    mainSb.AppendLine(kpv.Key + "=");

                foreach (var kpv in plChanges) {
                    // changes in each pl files
                    mainSb.AppendLine(string.Format("\r\n[{0}]", kpv.Key));
                    mainSb.Append(kpv.Value);
                }
            }

            File.WriteAllText(wcmPath, mainSb.ToString(), Encoding.Default);
        }

        /// <summary>
        ///     Get the letter to put on the .wcm according to the action
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        private string GetActionLetter(DeploymentAction action) {
            switch (action) {
                case DeploymentAction.Added:
                    return "A";
                case DeploymentAction.Replaced:
                    return "R";
                case DeploymentAction.Deleted:
                    return "D";
                default:
                    throw new ArgumentOutOfRangeException("action", action, null);
            }
        }

        /// <summary>
        ///     Create the AAA.prowcapp
        /// </summary>
        private void BuildCompleteProwcappFile() {
            // create the prowcapp file
            string content;
            if (!string.IsNullOrEmpty(_proEnv.WcProwcappModelPath) && File.Exists(_proEnv.WcProwcappModelPath))
                content = Utils.ReadAllText(_proEnv.WcProwcappModelPath, Encoding.Default);
            else
                content = Encoding.Default.GetString(_proEnv.FileContentProwcapp);

            // replace static variables
            content = content.Replace("%VENDORNAME%", _proEnv.WcVendorName);
            content = content.Replace("%APPLICATIONNAME%", _proEnv.WcApplicationName);
            content = content.Replace("%STARTUPPARAMETERS%", _proEnv.WcStartupParam);
            content = content.Replace("%PROWCAPPVERSION%", _proEnv.WcProwcappVersion.ToString());
            content = content.Replace("%LOCATORURL%", _proEnv.WcLocatorUrl);
            content = content.Replace("%PACKAGENAME%", _proEnv.WcPackageName);
            content = content.Replace("%CLIENTVERSION%", _proEnv.WcClientVersion);

            // replace %FILELIST%
            var sb = new StringBuilder();
            var cltNwkFolder = Path.Combine(_proEnv.TargetDirectory, _proEnv.ClientNwkDirectoryName);
            foreach (var file in Directory.EnumerateFiles(cltNwkFolder, "*", SearchOption.AllDirectories)) {
                var targetPath = file.Replace(cltNwkFolder, "").TrimStart('\\');
                sb.AppendLine(targetPath + "=");
            }

            content = content.Replace("%FILELIST%", sb.ToString());

            // replace %VERSION%
            sb.Clear();
            // list existing version
            foreach (var previousDeployement in ListPreviousDeployements.GroupBy(config => config.WcProwcappVersion).Select(group => group.First()))
                sb.AppendLine(string.Format("{0}={1}", previousDeployement.WcProwcappVersion, previousDeployement.WcPackageName));
            // add the current version
            sb.AppendLine(string.Format("{0}={1}", _proEnv.WcProwcappVersion, _proEnv.WcPackageName));
            // list how to update from previous versions
            foreach (var diffCab in _diffCabs.OrderBy(diffCab => diffCab.VersionToUpdateFrom)) {
                sb.AppendLine(string.Format("\r\n[{0}]", diffCab.VersionToUpdateFrom));
                sb.AppendLine("EndUserDescription=");
                sb.AppendLine(string.Format("\r\n[{0}Updates]", diffCab.VersionToUpdateFrom));
                sb.AppendLine(string.Format("{0}=diffs/{1}", _proEnv.WcApplicationName, Path.GetFileName(diffCab.CabPath)));
            }

            content = content.Replace("%VERSION%", sb.ToString());

            File.WriteAllText(Path.Combine(_proEnv.TargetDirectory, _proEnv.ClientWcpDirectoryName, _proEnv.WcApplicationName + ".prowcapp"), content, Encoding.Default);
        }

        /// <summary>
        ///     Can list the files in a directory and return the List of files to deploy to zip all the files of this directory
        ///     into a .cab
        /// </summary>
        private List<FileToDeploy> ListFilesInDirectoryToDeployInCab(string directoryPath, string cabPath) {
            // build the list of files to deploy
            var filesToCab = new List<FileToDeploy>();
            foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)) {
                var targetPath = file.Replace(directoryPath, "").TrimStart('\\');
                targetPath = Path.Combine(cabPath, targetPath);
                filesToCab.Add(new FileToDeployCab(file, Path.GetDirectoryName(targetPath), null).Set(file, targetPath));
            }

            return filesToCab;
        }

        #endregion
    }
}