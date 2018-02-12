﻿#region header
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
using System.Windows.Forms;
using abldeployer.Core;
using csdeployer.Form;
using csdeployer.Html;
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
            string currentOperationName;
            switch (_deployment.CurrentOperationName) {
                case DeploymentStep.CopyingReference:
                    currentOperationName = "Copie du paquet de référence";
                    break;;
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
                ConfigXml.Instance = ConfigXml.Load(csdeployer.Start.XmlConfigPath);
                ConfigXml.Instance.ReturnCode = Config.ReturnCode.NoSet;
                ConfigXml.Instance.DeploymentDateTime = DateTime.Now;
            } catch (Exception e) {
                MessageBox.Show("Impossible de lire le fichier xml d'entrée : " + csdeployer.Start.XmlConfigPath.Quoter() + "\r\n\r\n" + e, "Une erreur est survenue", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                if (ConfigXml.Instance.ReturnCode == Config.ReturnCode.NoSet) {
                    ConfigXml.Instance.ReturnCode = Config.ReturnCode.Error;
                }
                if (ConfigXml.Instance.DeployedFiles == null || ConfigXml.Instance.DeployedFiles.Count == 0) {
                    ConfigXml.Instance.DeployedFiles = _previousDeployedFiles;
                }
                ConfigXml.Save(ConfigXml.Instance, ConfigXml.Instance.OutPathDeploymentResults);
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
            Utils.DeleteDirectory(ConfigXml.Instance.FolderTemp, true);
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
                var prevXmlList = ConfigXml.Instance.PreviousDeploymentFiles;
                if (prevXmlList != null && prevXmlList.Count > 0) {
                    foreach (var xml in prevXmlList) {
                        if (File.Exists(xml)) {
                            var config = ConfigXml.Load(xml);
                            config.ExportXmlFile = xml;
                            listPreviousDeployedFiles.Add(config);
                        }
                    }

                    // sort the deployment from oldest (0) to newer (n)
                    listPreviousDeployedFiles = listPreviousDeployedFiles.OrderBy(config => config.WcProwcappVersion).ThenBy(config => config.DeploymentDateTime).ToList();
                    var lastOrDefault = listPreviousDeployedFiles.LastOrDefault();
                    if (lastOrDefault != null) {
                        _previousDeployedFiles = ConfigXml.Load(lastOrDefault.ExportXmlFile).DeployedFiles;
                    }
                }
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Erreur pendant le chargement des fichiers xml de déploiements précédents");
                return false;
            }

            switch (ConfigXml.Instance.RunMode) {
                case Config.RunMode.Deployment:
                    _deployment = new DeploymentHandlerDifferential(ConfigXml.Instance);
                    break;
                case Config.RunMode.Packaging:
                    _deployment = new DeploymentHandlerPackaging(ConfigXml.Instance);
                    ((DeploymentHandlerPackaging) _deployment).ListPreviousDeployements = listPreviousDeployedFiles.ToList();
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
                Utils.CreateDirectory(ConfigXml.Instance.OutPathReportDir);
                ExportReport(Path.Combine(ConfigXml.Instance.OutPathReportDir, "index.html"));
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
                if (ConfigXml.Instance.ReturnCode == Config.ReturnCode.Ok && !ConfigXml.Instance.IsTestMode) {
                    ConfigXml.Instance.DeployedFiles = _deployment.DeployedFilesOutput;
                    ConfigXml.Instance.CompilationErrors = _deployment.CompilationErrorsOutput;

                }
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Erreur survenue pendant la sauvegarde des fichiers déployés");
            }

            // we can now end the treatment
            Stop();
        }

        private void OnExecutionOk(DeploymentHandler deploymentHandler) {
            ConfigXml.Instance.ReturnCode = Config.ReturnCode.Ok;
        }

        private void OnExecutionFailed(DeploymentHandler deploymentHandler) {
            if (deploymentHandler.HasBeenCancelled) {
                ConfigXml.Instance.ReturnCode = Config.ReturnCode.Canceled;
            } else {
                ConfigXml.Instance.ReturnCode = Config.ReturnCode.Error;
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
            switch (ConfigXml.Instance.ReturnCode) {
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
                html.AppendLine(FormatDeploymentParameters(_deployment));
            } catch (Exception e) {
                ErrorHandler.LogErrors(e, "Erreur lors de la génération des paramètres de déploiement");
            }

            if (!string.IsNullOrEmpty(ConfigXml.Instance.RaisedException)) {
                html.AppendLine(@"<h2>Problèmes rencontrés pendant le déploiement :</h2>");
                html.AppendLine(@"<div class='IndentDiv errors'>");
                html.AppendLine(ConfigXml.Instance.RaisedException);
                html.AppendLine(@"</div>");
                html.AppendLine(@"<div class='IndentDiv'>");
                html.AppendLine(@"<b>Lien vers le fichier .log pour plus de détails :</b><br>");
                html.AppendLine(ConfigXml.Instance.ErrorLogFilePath.ToHtmlLink());
                html.AppendLine(@"</div>");
            }

            if (ConfigXml.Instance.RuleErrors != null && ConfigXml.Instance.RuleErrors.Count > 0) {
                html.AppendLine(@"<h2>Erreurs de règles :</h2>");
                html.AppendLine(@"<div class='IndentDiv errors'>");
                foreach (var fileErrors in ConfigXml.Instance.RuleErrors) {
                    foreach (var error in fileErrors.Item2) {
                        html.AppendLine(HtmlDeployRule.Description(fileErrors.Item1, error.Item1) + " : " + error.Item2);
                    }
                }
                html.AppendLine(@"</div>");
            }

            try {
                html.AppendLine(FormatDeploymentResults(_deployment));
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

        private static string FormatDeploymentParameters(DeploymentHandler theDeployment) {
            var sb = new StringBuilder(@"             
                <h2>Paramètres du déploiement :</h2>
                <div class='IndentDiv'>
                    <div>Date de début de déploiement : <b>" + theDeployment.StartingTime + @"</b></div>
                    <div>Date de début de compilation : <b>" + theDeployment.ProCompilation.StartingTime + @"</b></div>
                    <div>Nombre de processeurs sur cet ordinateur : <b>" + Environment.ProcessorCount + @"</b></div>
                    <div>Nombre de process progress utilisés pour la compilation : <b>" + theDeployment.ProCompilation.TotalNumberOfProcesses + @"</b></div>
                    <div>Compilation forcée en mono-process? : <b>" + theDeployment.ProCompilation.MonoProcess + (theDeployment.ProEnv.IsDatabaseSingleUser ? " (connecté à une base de données en mono-utilisateur!)" : "") + @"</b></div>
                    <div>Répertoire des sources : " + theDeployment.ProEnv.SourceDirectory.ToHtmlLink() + @"</div>
                    <div>Répertoire cible pour le déploiement : " + theDeployment.ProEnv.TargetDirectory.ToHtmlLink() + @"</div>       
                </div>");
            var deployment1 = theDeployment as DeploymentHandlerDifferential;
            if (deployment1 != null) {
                sb.Append(@"
                <div class='IndentDiv'>
                    <div>Déploiement FULL : <b>" + deployment1.ForceFullDeploy + @"</b></div>
                    <div>Calcul du MD5 des fichiers : <b>" + deployment1.ComputeMd5 + @"</b></div> 
                </div>");
            }
            var deployment2 = theDeployment as DeploymentHandlerPackaging;
            if (deployment2 != null) {
                sb.Append(@"
                <div class='IndentDiv'>
                    <div>Répertoire de référence : " + deployment2.ReferenceDirectory.ToHtmlLink() + @"</div>
                </div>");
            }
            return sb.ToString();
        }

        private static string FormatDeploymentResults(DeploymentHandler theDeployment) {
            StringBuilder currentReport = new StringBuilder();
            
            var deployment2 = theDeployment as DeploymentHandlerPackaging;
            if (deployment2 != null) {
                if (deployment2.WebClientCreated) {
                    currentReport.AppendLine(@"             
                <h2>Fichiers webclient créés :</h2>
                <div class='IndentDiv'>");
                    //sb.AppendLine(@"<h3>Fichier " + _proEnv.WcApplicationName + @".cab webclient complet créé</h3>");
                    currentReport.AppendLine(@"<div><img height='15px' src='" + Utils.GetExtensionImage("pl", true) + "'>" + Path.Combine(deployment2.ProEnv.TargetDirectory, deployment2.ProEnv.ClientWcpDirectoryName, deployment2.ProEnv.WcApplicationName + ".prowcapp").ToHtmlLink(null, true) + @"</div>");
                    currentReport.AppendLine(@"<div><img height='15px' src='" + Utils.GetExtensionImage("pl", true) + "'>" + Path.Combine(deployment2.ProEnv.TargetDirectory, deployment2.ProEnv.ClientWcpDirectoryName, deployment2.ProEnv.WcApplicationName + ".cab").ToHtmlLink(null, true) + @"</div>");
                    if (deployment2.DiffCabs.Count > 0) {
                        //sb.AppendLine(@"<h3>Fichier(s) .cab diffs webclient créé(s)</h3>");
                        foreach (var cab in deployment2.DiffCabs) {
                            currentReport.AppendLine(@"<div><img height='15px' src='" + Utils.GetExtensionImage("pl", true) + "'>" + cab.CabPath.ToHtmlLink(null, true) + @"</div>");
                        }
                    }
                    currentReport.AppendLine(@"</div>");
                } else if (deployment2.ListPreviousDeployements != null && deployment2.ListPreviousDeployements.Count > 0) {
                    var prevWcpDir = Path.Combine(deployment2.ReferenceDirectory, deployment2.ProEnv.ClientWcpDirectoryName);
                    if (Directory.Exists(prevWcpDir)) {
                        currentReport.AppendLine(@"             
                        <h2>Fichiers webclient copiés :</h2>
                        <div class='IndentDiv'>");
                        currentReport.AppendLine(@"<div>Pas de différences au niveau client sur ce paquet, copie du dossier webclient du dernier paquet</div>");
                        currentReport.AppendLine(@"<div>Dossier du dernier paquet : " + prevWcpDir.ToHtmlLink() + "</div>");
                        currentReport.AppendLine(@"<div>Dossier de ce paquet : " + Path.Combine(deployment2.ProEnv.TargetDirectory, deployment2.ProEnv.ClientWcpDirectoryName).ToHtmlLink() + "</div>");
                        currentReport.AppendLine(@"</div>");
                    }
                }
            }

            currentReport.Append(@"<h2>Détails sur le déploiement :</h2>");
            currentReport.Append(@"<div class='IndentDiv'>");

            if (theDeployment.HasBeenCancelled) {
                // the process has been canceled
                currentReport.Append(@"<div><img style='padding-right: 20px;' src='Warning_25x25' height='15px'>Déploiement annulé par l'utilisateur</div>");
            } else if (theDeployment.CompilationHasFailed) {
                // provide info on the possible error!
                currentReport.Append(@"<div><img style='padding-right: 20px;' src='Error_25x25' height='15px'>Un process progress a fini en erreur, déploiement arrêté</div>");

                if (theDeployment.ProCompilation.CompilationFailedOnMaxUser) {
                    currentReport.Append(@"<div><img style='padding-right: 20px;' src='Help_25x25' height='15px'>One or more processes started for this compilation tried to connect to the database and failed because the maximum number of connection has been reached (error 748). To correct this problem, you can either :<br><li>reduce the number of processes to use for each core of your computer</li><li>or increase the maximum of connections for your database (-n parameter in the PROSERVE command)</li></div>");
                }
            } else if (theDeployment.DeploymentErrorOccured) {
                currentReport.Append(@"<div><img style='padding-right: 20px;' src='Error_25x25' height='15px'>Le déploiement a échoué</div>");
            }

            var listLinesCompilation = new List<Tuple<int, string>>();
            StringBuilder line = new StringBuilder();

            var totalDeployedFiles = 0;
            var nbDeploymentError = 0;
            var nbCompilationError = 0;
            var nbCompilationWarning = 0;

            // compilation errors
            foreach (var fileInError in theDeployment.ProCompilation.ListFilesToCompile.Where(file => file.Errors != null)) {
                bool hasError = fileInError.Errors.Exists(error => error.Level >= ErrorLevel.Error);
                bool hasWarning = fileInError.Errors.Exists(error => error.Level < ErrorLevel.Error);

                if (hasError || hasWarning) {
                    // only add compilation errors
                    line.Clear();
                    line.Append("<div %ALTERNATE%style=\"background-repeat: no-repeat; background-image: url('" + (hasError ? "Error_25x25" : "Warning_25x25") + "'); padding-left: 35px; padding-top: 6px; padding-bottom: 6px;\">");
                    line.Append(FormatCompilationResultForSingleFile(fileInError.SourcePath, fileInError, null));
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
            foreach (var kpv in theDeployment.FilesToDeployPerStep) {
                // group either by directory name or by pack name
                var groupDirectory = kpv.Value.GroupBy(deploy => deploy.GroupKey).Select(deploys => deploys.ToList()).ToList();

                foreach (var group in groupDirectory.OrderByDescending(list => list.First().DeployType).ThenBy(list => list.First().GroupKey)) {
                    var deployFailed = group.Exists(deploy => !deploy.IsOk);
                    var first = group.First();

                    line.Clear();
                    line.Append("<div %ALTERNATE%style=\"background-repeat: no-repeat; background-image: url('" + (deployFailed ? "Error_25x25" : "Ok_25x25") + "'); padding-left: 35px; padding-top: 6px; padding-bottom: 6px;\">");
                    line.Append(HtmlFileToDeploy.GroupHeader(first));
                    foreach (var fileToDeploy in group.OrderBy(deploy => deploy.To)) {
                        line.Append(HtmlFileToDeploy.Description(fileToDeploy, kpv.Key <= 1 ? theDeployment.ProEnv.SourceDirectory : theDeployment.ProEnv.TargetDirectory));
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
            currentReport.Append(@"<div style='padding-top: 7px; padding-bottom: 7px;'>Nombre de fichiers compilés : <b>" + theDeployment.ProCompilation.NbFilesToCompile + "</b>, répartition : " + Utils.GetNbFilesPerType(theDeployment.ProCompilation.ListFilesToCompile.Select(compile => compile.SourcePath).ToList()).Aggregate("", (current, kpv) => current + (@"<img style='padding-right: 5px;' src='" + Utils.GetExtensionImage(kpv.Key.ToString(), true) + "' height='15px'><span style='padding-right: 12px;'>x" + kpv.Value + "</span>")) + "</div>");

            // compilation time
            currentReport.Append(@"<div><img style='padding-right: 20px;' src='Clock_15px' height='15px'>Temps de compilation total : <b>" + Utils.ConvertToHumanTime(theDeployment.ProCompilation.TotalCompilationTime) + @"</b></div>");

            if (nbCompilationError > 0)
                currentReport.Append("<div><img style='padding-right: 20px;' src='Error_25x25' height='15px'>Nombre de fichiers avec erreur(s) de compilation : " + nbCompilationError + "</div>");
            if (nbCompilationWarning > 0)
                currentReport.Append("<div><img style='padding-right: 20px;' src='Warning_25x25' height='15px'>Nombre de fichiers avec avertissement(s) de compilation : " + nbCompilationWarning + "</div>");
            if (theDeployment.ProCompilation.NumberOfFilesTreated - nbCompilationError - nbCompilationWarning > 0)
                currentReport.Append("<div><img style='padding-right: 20px;' src='Ok_25x25' height='15px'>Nombre de fichiers compilés correctement : " + (theDeployment.ProCompilation.NumberOfFilesTreated - nbCompilationError - nbCompilationWarning) + "</div>");

            // deploy
            currentReport.Append(@"<div style='padding-top: 7px; padding-bottom: 7px;'>Nombre de fichiers déployés : <b>" + totalDeployedFiles + "</b>, répartition : " + Utils.GetNbFilesPerType(theDeployment.FilesToDeployPerStep.SelectMany(pair => pair.Value).Select(deploy => deploy.To).ToList()).Aggregate("", (current, kpv) => current + (@"<img style='padding-right: 5px;' src='" + Utils.GetExtensionImage(kpv.Key.ToString(), true) + "' height='15px'><span style='padding-right: 12px;'>x" + kpv.Value + "</span>")) + "</div>");

            // deployment time
            currentReport.Append(@"<div><img style='padding-right: 20px;' src='Clock_15px' height='15px'>Temps de déploiement total : <b>" + Utils.ConvertToHumanTime(theDeployment.TotalDeploymentTime) + @"</b></div>");

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

            var deployment1 = theDeployment as DeploymentHandlerDifferential;
            if (deployment1 != null) {
                currentReport.AppendLine(@"             
                <h2>Informations complémentaires sur le listing :</h2>
                <div class='IndentDiv'>");
                var deployedFiles = deployment1.DeployedFilesOutput.Select(deployed => deployed.SourcePath).ToList();
                deployedFiles.AddRange(deployment1.DeployedFilesOutput.Where(deployed => deployed is FileDeployedCompiled).Cast<FileDeployedCompiled>().SelectMany(deployed => deployed.RequiredFiles).Select(info => info.SourcePath));
                var sourceFilesNotDeployed = deployment1.SourceFiles.Select(pair => pair.Key.Replace(deployment1.ProEnv.SourceDirectory.CorrectDirPath(), "")).Where(sourceFilePath => !deployedFiles.Exists(path => path.Equals(sourceFilePath))).ToList();
                currentReport.AppendLine(@"             
                    <div>Nombre de fichiers non déployés trouvés (avant action) : <b>" + deployment1.SourceFilesNew.Count + @"</b></div>
                    <div>Nombre de fichiers identiques au dernier déploiement : <b>" + deployment1.SourceFilesUpToDate.Count + @"</b></div> 
                    <div>Nombre de fichiers manquants par rapport au dernier déploiement : <b>" + deployment1.SourceFilesMissing.Count + @"</b></div>
                    <div>Nombre de fichiers source non utilisés pour ce déploiement : <b>" + sourceFilesNotDeployed.Count + @"</b></div>
            ");
                if (sourceFilesNotDeployed.Count > 0) {
                    currentReport.AppendLine(@"<h3>Liste des fichiers non utilisés pour ce déploiement</h3>");
                    currentReport.AppendLine(@"<div class='IndentDiv'>");
                    foreach (var undeployedFile in sourceFilesNotDeployed) {
                        currentReport.AppendLine(@"<div><img height='15px' src='" + Utils.GetExtensionImage((Path.GetExtension(undeployedFile) ?? "").Replace(".", ""), false) + "'>" + Path.Combine(deployment1.ProEnv.SourceDirectory, undeployedFile).ToHtmlLink(undeployedFile, true) + @"</div>");
                    }
                    currentReport.AppendLine(@"</div>");
                }
                currentReport.AppendLine(@"</div>");
            }

            return currentReport.ToString();
        }

        /// <summary>
        /// Allows to format a small text to explain the errors found in a file and the generated files...
        /// </summary>
        private static string FormatCompilationResultForSingleFile(string sourceFilePath, FileToCompile fileToCompile, List<FileToDeploy> listDeployedFiles) {
            var line = new StringBuilder();

            line.Append("<div style='padding-bottom: 5px;'>");
            line.Append("<img height='15px' src='" + Utils.GetExtensionImage((Path.GetExtension(sourceFilePath) ?? "").Replace(".", "")) + "'>");
            line.Append("<b>" + sourceFilePath.ToHtmlLink(Path.GetFileName(sourceFilePath), true) + "</b> in " + Path.GetDirectoryName(sourceFilePath).ToHtmlLink());
            line.Append("</div>");

            if (fileToCompile != null && fileToCompile.Errors != null) {
                line.Append("<div style='padding-left: 10px; padding-bottom: 5px;'>");
                foreach (var error in fileToCompile.Errors) {
                    line.Append(HtmlFileError.Description(error));
                }
                line.Append("</div>");
            }

            if (listDeployedFiles != null) {
                line.Append("<div>");
                // group either by directory name or by pack name
                var groupDirectory = listDeployedFiles.GroupBy(deploy => deploy.GroupKey).Select(deploys => deploys.ToList()).ToList();
                foreach (var group in groupDirectory.OrderByDescending(list => list.First().DeployType).ThenBy(list => list.First().GroupKey)) {
                    line.Append(HtmlFileToDeploy.GroupHeader(group.First()));
                    foreach (var fileToDeploy in group.OrderBy(deploy => deploy.To)) {
                        line.Append(HtmlFileToDeploy.Description(fileToDeploy));
                    }
                }
                line.Append("</div>");
            }

            return line.ToString();
        }

        #endregion
    }
}