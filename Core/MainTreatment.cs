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
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using csdeployer.Core.Deploy;
using csdeployer.Form;
using csdeployer.Lib;
using csdeployer._Resource;

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
            return new ProgressionEventArgs {
                GlobalProgression = _deployment.OverallProgressionPercentage / _deployment.TotalNumberOfOperations,
                ElpasedTime = _deployment.ElapsedTime,
                CurrentStepName = _deployment.CurrentOperationName,
                CurrentStepProgression = _deployment.CurrentOperationPercentage
            };
        }

        /// <summary>
        /// Called when the treatment starts
        /// </summary>
        protected override void StartJob() {

            // load the input .xml
            try {
                Config.Instance = Config.Load(csdeployer.Start.XmlConfigPath);
                Config.Instance.ReturnCode = Config.ReturnCode.NoSet;
                Config.Instance.DeploymentDateTime = DateTime.Now;
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Impossible de lire le fichier xml d'entrée : " + csdeployer.Start.XmlConfigPath.Quoter());
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
                if (Config.Instance.ReturnCode == Config.ReturnCode.NoSet) {
                    Config.Instance.ReturnCode = Config.ReturnCode.Error;
                }
                if (Config.Instance.DeployedFiles == null || Config.Instance.DeployedFiles.Count == 0) {
                    Config.Instance.DeployedFiles = _previousDeployedFiles;
                }
                Config.Save(Config.Instance, Config.Instance.OutPathDeploymentResults);
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
            Utils.DeleteDirectory(Config.Instance.FolderTemp, true);
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
            var listPreviousDeployedFiles = new List<Config.ProConfig>();
            try {
                var prevXmlList = Config.Instance.PreviousDeploymentFiles;
                if (prevXmlList != null && prevXmlList.Count > 0) {
                    foreach (var xml in prevXmlList) {
                        if (File.Exists(xml)) {
                            var config = Config.Load(xml);
                            config.ExportXmlFile = xml;
                            listPreviousDeployedFiles.Add(config);
                        }
                    }

                    // sort the deployment from oldest (0) to newer (n)
                    listPreviousDeployedFiles = listPreviousDeployedFiles.OrderBy(config => config.WcProwcappVersion).ThenBy(config => config.DeploymentDateTime).ToList();
                    var lastOrDefault = listPreviousDeployedFiles.LastOrDefault();
                    if (lastOrDefault != null) {
                        _previousDeployedFiles = Config.Load(lastOrDefault.ExportXmlFile).DeployedFiles;
                    }
                }
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Erreur pendant le chargement des fichiers xml de déploiements précédents");
                return false;
            }

            switch (Config.Instance.RunMode) {
                case Config.RunMode.Deployment:
                    _deployment = new DeploymentHandlerDifferential(Config.Instance);
                    break;
                case Config.RunMode.Packaging:
                    _deployment = new DeploymentHandlerPackaging(Config.Instance);
                    ((DeploymentHandlerPackaging) _deployment).ListPreviousDeployements = listPreviousDeployedFiles.ToList();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            _deployment.PreviousDeployedFiles = _previousDeployedFiles;
            _deployment.OnExecutionEnd = OnExecutionEnd;
            _deployment.OnExecutionOk = OnExecutionOk;
            _deployment.OnExecutionFailed = OnExecutionFailed;

            return _deployment.Start();
        }

        /// <summary>
        /// Stop deploying
        /// </summary>
        private void StopDeploying() {
            // we create the html report
            try {
                Utils.CreateDirectory(Config.Instance.OutPathReportDir);
                ExportReport(Path.Combine(Config.Instance.OutPathReportDir, "index.html"));
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
                if (Config.Instance.ReturnCode == Config.ReturnCode.Ok && !Config.Instance.IsTestMode) {
                    Config.Instance.DeployedFiles = _deployment.DeployedFilesOutput;
                    Config.Instance.CompilationErrors = _deployment.CompilationErrorsOutput;

                }
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Erreur survenue pendant la sauvegarde des fichiers déployés");
            }

            // we can now end the treatment
            Stop();
        }

        private void OnExecutionOk(DeploymentHandler deploymentHandler) {
            Config.Instance.ReturnCode = Config.ReturnCode.Ok;
        }

        private void OnExecutionFailed(DeploymentHandler deploymentHandler) {
            if (deploymentHandler.HasBeenCancelled) {
                Config.Instance.ReturnCode = Config.ReturnCode.Canceled;
            } else {
                Config.Instance.ReturnCode = Config.ReturnCode.Error;
            }
        }

        private void ExportReport(string path) {
            var html = new StringBuilder();
            html.AppendLine("<html class='NormalBackColor'>");
            html.AppendLine("<head>");
            html.AppendLine("<meta charset='UTF-8'>");
            html.AppendLine("<style>");
            html.AppendLine(HtmlResources.StyleSheet);
            html.AppendLine("</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");

            html.AppendLine(@"
                <table class='ToolTipName' style='margin-bottom: 0px; width: 100%'>
                    <tr>
                        <td rowspan='2' style='width: 95px; padding-left: 10px'><img src='Report_64x64' width='64' height='64' /></td>
                        <td class='Title'>Rapport de déploiement</td>
                    </tr>
                    <tr>");
            switch (Config.Instance.ReturnCode) {
                case Config.ReturnCode.Error:
                    html.AppendLine(@"<td class='SubTitle'><img style='padding-right: 2px;' src='Error_25x25' height='25px'>Une erreur est survenue</td>");
                    break;
                case Config.ReturnCode.Ok:
                    if (_deployment.IsTestMode)
                        html.AppendLine(@"<td class='SubTitle'><img style='padding-right: 2px;' src='Test_25x25' height='25px'>Test réalisé avec succès</td>");
                    else
                        html.AppendLine(@"<td class='SubTitle'><img style='padding-right: 2px;' src='Ok_25x25' height='25px'>Le déploiement s'est déroulé correctement</td>");
                    break;
                case Config.ReturnCode.Canceled:
                    html.AppendLine(@"<td class='SubTitle'><img style='padding-right: 2px;' src='Warning_25x25' height='25px'>Déploiement annulé par l'utilisateur</td>");
                    break;
            }
            html.AppendLine(@"
                    </tr>
                </table>");

            try {
                html.AppendLine(_deployment.FormatDeploymentParameters());
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Erreur lors de la génération des paramètres de déploiement");
            }

            if (!string.IsNullOrEmpty(Config.Instance.RaisedException)) {
                html.AppendLine(@"<h2>Problèmes rencontrés pendant le déploiement :</h2>");
                html.AppendLine(@"<div class='IndentDiv errors'>");
                html.AppendLine(Config.Instance.RaisedException);
                html.AppendLine(@"</div>");
                html.AppendLine(@"<div class='IndentDiv'>");
                html.AppendLine(@"<b>Lien vers le fichier .log pour plus de détails :</b><br>");
                html.AppendLine(Config.Instance.ErrorLogFilePath.ToHtmlLink());
                html.AppendLine(@"</div>");
            }

            if (!string.IsNullOrEmpty(Config.Instance.RuleErrors)) {
                html.AppendLine(@"<h2>Erreurs de règles :</h2>");
                html.AppendLine(@"<div class='IndentDiv errors'>");
                html.AppendLine(Config.Instance.RuleErrors);
                html.AppendLine(@"</div>");
            }

            try {
                html.AppendLine(_deployment.FormatDeploymentResults());
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Erreur lors de la génération des résultats de déploiement");
            }

            html.AppendLine("</body>");
            html.AppendLine("</html>");

            var regex1 = new Regex("src=[\"'](.*?)[\"']", RegexOptions.Compiled);
            foreach (Match match in regex1.Matches(html.ToString())) {
                if (match.Groups.Count >= 2) {
                    var imgFile = Path.Combine(Path.GetDirectoryName(path) ?? "", match.Groups[1].Value);
                    if (!File.Exists(imgFile)) {
                        var tryImg = (Image) ImageResources.ResourceManager.GetObject(match.Groups[1].Value);
                        if (tryImg != null) {
                            tryImg.Save(imgFile);
                        }
                    }
                }
            }

            regex1 = new Regex("<a href=\"(.*?)[|\"]", RegexOptions.Compiled);
            Utils.FileWriteAllText(path, regex1.Replace(html.ToString(), "<a href=\"file:///$1\""), Encoding.UTF8);
        }

        #endregion
    }
}