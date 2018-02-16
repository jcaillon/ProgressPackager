#region header
// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (MainTreatment.cs) is part of csdeployer.
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
using System.Threading.Tasks;
using System.Windows.Forms;
using abldeployer.Core;
using abldeployer.Core.Config;
using csdeployer.Form;
using csdeployer.Html;
using csdeployer.Lib;

namespace csdeployer.Core {

    internal class MainTreatment : ProgressTreatment {

        #region Override

        /// <summary>
        /// Should return the progression of the treatment
        /// </summary>
        /// <returns></returns>
        protected override ProgressionEventArgs GetProgress() {
            if (_deployment == null)
                return new ProgressionEventArgs {
                    GlobalProgression = 0,
                    CurrentStepProgression = 0,
                    CurrentStepName = "Initialisation du déploiement",
                    ElpasedTime = ElapsedTime
                };
            string currentOperationName;
            switch (_deployment.CurrentOperationName) {
                case DeploymentStep.CopyingReference:
                    currentOperationName = "Copie du paquet de référence";
                    break;
                case DeploymentStep.Listing:
                    currentOperationName = "Listing des fichiers sources";
                    break;
                case DeploymentStep.Compilation:
                    currentOperationName = "Compilation";
                    break;
                case DeploymentStep.DeployRCode:
                    currentOperationName = "Déploiement des rcodes";
                    break;
                case DeploymentStep.DeployFile:
                    currentOperationName = "Déploiement des fichiers";
                    break;
                case DeploymentStep.CopyingFinalPackageToDistant:
                    currentOperationName = "Copie du paquet sur répertoire final";
                    break;
                case DeploymentStep.BuildingWebclientDiffs:
                    currentOperationName = "Création des cab webclient différentiels";
                    break;
                case DeploymentStep.BuildingWebclientCompleteCab:
                    currentOperationName = "Création du cab webclient complet";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return new ProgressionEventArgs {
                GlobalProgression = _deployment.OverallProgressionPercentage / _deployment.TotalNumberOfOperations,
                ElpasedTime = ElapsedTime,
                CurrentStepName = currentOperationName,
                CurrentStepProgression = _deployment.CurrentOperationPercentage
            };
        }

        /// <summary>
        /// Called when the treatment starts
        /// </summary>
        protected override void StartJob() {

            // load the input .xml
            try {
                CsConfigDeploymentPackaging.Instance = CsDeployerConfig.Load(csdeployer.Start.XmlConfigPath);
                CsConfigDeploymentPackaging.Instance.ReturnCode = ReturnCode.NoSet;
                CsConfigDeploymentPackaging.Instance.DeploymentDateTime = DateTime.Now;
            } catch (Exception e) {
                MessageBox.Show(@"Impossible de lire le fichier xml d'entrée : " + csdeployer.Start.XmlConfigPath.Quoter() + Environment.NewLine + Environment.NewLine + e, @"Une erreur est survenue", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Stop();
                return;
            }

            // start deploying
            Task.Factory.StartNew(() => {
                try {
                    if (StartDeploying()) {
                        return;
                    }
                } catch (Exception e) {
                    ErrorHandler.LogErrors(e, "Erreur lors de l'initialisation du déploiement");
                }
                Stop();
            });
        }

        /// <summary>
        /// Called when the treatment stops
        /// </summary>
        protected override void StopJob() {

            // save the output xml
            try {
                if (CsConfigDeploymentPackaging.Instance.ReturnCode == ReturnCode.NoSet) {
                    CsConfigDeploymentPackaging.Instance.ReturnCode = ReturnCode.Error;
                }
                CsDeployerConfig.Save(CsConfigDeploymentPackaging.Instance, CsConfigDeploymentPackaging.Instance.OutPathDeploymentResults);
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Erreur lors de l'enregistrement du fichier de sortie");
            }

            // stop deploying
            try {
                StopDeploying();
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Erreur survenue pendant la finalisation du déploiement");
            }

            // get rid of the temp folder
            Utils.DeleteDirectory(CsConfigDeploymentPackaging.Instance.FolderTemp, true);
        }

        /// <summary>
        /// Method to call to cancel the treatment
        /// </summary>
        public override void Cancel() {
            try {
                if (_deployment != null) {
                    _deployment.Cancel();
                    return;
                }
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Erreur pendant l'annulation du déploiement");
            }
            Stop();
        }

        #endregion

        #region Private fields
        
        private DeploymentHandlerDifferential _deployment;

        private DateTime _startingTime;

        private List<FileDeployed> _previousDeployedFiles;

        #endregion

        #region Private methods

        /// <summary>
        /// Get the time elapsed since the beginning of the compilation in a human readable format
        /// </summary>
        private string ElapsedTime {
            get { return Utils.ConvertToHumanTime(TimeSpan.FromMilliseconds(DateTime.Now.Subtract(_startingTime).TotalMilliseconds)); }
        }

        /// <summary>
        /// Start deploying
        /// </summary>
        private bool StartDeploying() {
            _startingTime = DateTime.Now;

            // read the previous source files
            var listPreviousDeployedFiles = new List<CsConfigDeploymentPackaging>();
            try {
                var prevXmlList = CsConfigDeploymentPackaging.Instance.PreviousDeploymentFiles;
                if (prevXmlList != null && prevXmlList.Count > 0) {
                    foreach (var xmlPath in prevXmlList) {
                        if (File.Exists(xmlPath)) {
                            var config = CsDeployerConfig.Load(xmlPath);
                            config.ExportXmlFile = xmlPath;
                            listPreviousDeployedFiles.Add(config);
                        }
                    }

                    // sort the deployment from oldest (0) to newer (n)
                    listPreviousDeployedFiles = listPreviousDeployedFiles.OrderBy(config => config.WcProwcappVersion).ThenBy(config => config.DeploymentDateTime).ToList();
                    var lastOrDefault = listPreviousDeployedFiles.LastOrDefault();
                    if (lastOrDefault != null) {
                        _previousDeployedFiles = CsDeployerConfig.Load(lastOrDefault.ExportXmlFile).DeployedFiles;
                    }
                }
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Erreur pendant le chargement des fichiers xml de déploiements précédents");
                return false;
            }

            switch (CsConfigDeploymentPackaging.Instance.RunMode) {
                case RunMode.Deployment:
                    _deployment = new DeploymentHandlerDifferential(CsConfigDeploymentPackaging.Instance);
                    break;
                case RunMode.Packaging:
                    _deployment = new DeploymentHandlerPackaging(CsConfigDeploymentPackaging.Instance);
                    ((DeploymentHandlerPackaging) _deployment).ListPreviousDeployements = listPreviousDeployedFiles.Cast<ConfigDeploymentPackaging>().ToList();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            _deployment.PreviousDeployedFiles = _previousDeployedFiles;
            _deployment.OnExecutionEnd = OnExecutionEnd;
            _deployment.OnExecutionOk = OnExecutionOk;
            _deployment.OnExecutionFailed = OnExecutionFailed;

            _deployment.Start();

            return true;
        }

        /// <summary>
        /// Stop deploying
        /// </summary>
        private void StopDeploying() {
            // we create the html report
            try {
                Utils.CreateDirectory(CsConfigDeploymentPackaging.Instance.OutPathReportDir);
                HtmlReport.ExportReport(_deployment, Path.Combine(CsConfigDeploymentPackaging.Instance.OutPathReportDir, "index.html"));
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Erreur survenue pendant l'export du rapport html");
            }
        }

        /// <summary>
        /// On deployment end
        /// </summary>
        private void OnExecutionEnd(DeploymentHandler deploymentHandler) {
            // we save the data on the current source files
            try {
                if (CsConfigDeploymentPackaging.Instance.ReturnCode == ReturnCode.Ok && !CsConfigDeploymentPackaging.Instance.IsTestMode) {
                    CsConfigDeploymentPackaging.Instance.DeployedFiles = _deployment.DeployedFilesOutput;
                    CsConfigDeploymentPackaging.Instance.CompilationErrors = _deployment.CompilationErrorsOutput;
                }
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Erreur survenue pendant la sauvegarde des fichiers déployés");
            }

            // we can now end the treatment
            Stop();
        }

        private void OnExecutionOk(DeploymentHandler deploymentHandler) {
            CsConfigDeploymentPackaging.Instance.ReturnCode = ReturnCode.Ok;
        }

        private void OnExecutionFailed(DeploymentHandler deploymentHandler) {
            CsConfigDeploymentPackaging.Instance.ReturnCode = deploymentHandler.HasBeenCancelled ? ReturnCode.Canceled : ReturnCode.Error;
        }
        
        #endregion
    }
}