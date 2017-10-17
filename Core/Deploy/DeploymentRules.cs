#region header
// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (DeploymentRules.cs) is part of csdeployer.
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
using System.Text;
using csdeployer.Lib;

namespace csdeployer.Core.Deploy {
    internal static class DeploymentRules {
        #region GetRules

        public static List<DeployRule> GetRules(string confFilePath, out string outputMessage) {
            var rulesList = ReadConfigurationFile(confFilePath, out outputMessage);

            // sort the rules
            rulesList.Sort((item1, item2) => {
                // lower step first
                var compare = item1.Step.CompareTo(item2.Step);
                if (compare != 0)
                    return compare;

                var itemTransfer1 = item1 as DeployTransferRule;
                var itemTransfer2 = item2 as DeployTransferRule;

                if (itemTransfer1 != null && itemTransfer2 != null) {
                    // continue first
                    compare = itemTransfer2.ContinueAfterThisRule.CompareTo(itemTransfer1.ContinueAfterThisRule);
                    if (compare != 0)
                        return compare;

                    // copy last
                    compare = itemTransfer1.Type.CompareTo(itemTransfer2.Type);
                    if (compare != 0)
                        return compare;

                    // first line in first in
                    return itemTransfer1.Line.CompareTo(itemTransfer2.Line);
                }

                // filter before transfer
                return itemTransfer1 == null ? 1 : -1;
            });

            return rulesList;
        }

        #endregion

        #region ReadConfigurationFile

        /// <summary>
        /// Reads the given rule file
        /// </summary>
        public static List<DeployRule> ReadConfigurationFile(string path, out string readingErrorsHtml) {
            var outputMessage = new StringBuilder();

            // get all the rules
            var list = new List<DeployRule>();
            Utils.ForEachLine(path, new byte[0], (lineNb, lineString) => {
                try {
                    var items = lineString.Split('\t');

                    // new variable
                    if (items.Length == 2) {
                        var obj = new DeployVariableRule {
                            Source = path,
                            Line = lineNb + 1,
                            VariableName = items[0].Trim(),
                            Path = items[1].Trim()
                        };

                        if (!obj.VariableName.StartsWith("<") || !obj.VariableName.EndsWith(">")) {
                            outputMessage.AppendLine("La règle de type VARIABLE, ligne n°" + (lineNb + 1) + " est incorrecte, le format devrait être &lt;VAR&gt;<br>");
                            return;
                        }

                        if (!string.IsNullOrEmpty(obj.Path))
                            list.Add(obj);
                    }

                    int step = 0;
                    if (items.Length > 2 && !int.TryParse(items[0].Trim(), out step))
                        return;

                    // new transfer rule
                    if (items.Length >= 4) {
                        DeployType type;
                        if (Enum.TryParse(items[1].Trim(), true, out type)) {
                            var obj = DeployTransferRule.New(type);
                            obj.Source = path;
                            obj.Line = lineNb + 1;
                            obj.Step = step;
                            obj.ContinueAfterThisRule = items[2].Trim().EqualsCi("yes") || items[2].Trim().EqualsCi("true");
                            obj.SourcePattern = items[3].Trim();

                            var newRules = new List<DeployTransferRule> {obj};
                            if (items.Length > 4) {
                                var multipleTargets = items[4].Split('|');
                                obj.DeployTarget = multipleTargets[0].Trim().Replace('/', '\\');
                                for (int i = 1; i < multipleTargets.Length; i++) {
                                    DeployTransferRule copiedRule = obj.GetCopy();
                                    copiedRule.ContinueAfterThisRule = true;
                                    copiedRule.DeployTarget = multipleTargets[i].Trim().Replace('/', '\\');
                                    newRules.Add(copiedRule);
                                }
                            }

                            foreach (var rule in newRules) {
                                rule.ShouldDeployTargetReplaceDollar = rule.DeployTarget.StartsWith(":");
                                if (rule.ShouldDeployTargetReplaceDollar)
                                    rule.DeployTarget = rule.DeployTarget.Remove(0, 1);

                                string errorMsg;
                                var isOk = rule.IsValid(out errorMsg);
                                if (isOk) {
                                    list.Add(rule);
                                } else if (!string.IsNullOrEmpty(errorMsg)) {
                                    outputMessage.AppendLine(errorMsg + "<br>");
                                }
                            }
                        }
                    }

                    if (items.Length == 3) {
                        // new filter rule

                        var obj = new DeployFilterRule {
                            Source = path,
                            Line = lineNb + 1,
                            Step = step,
                            Include = items[1].Trim().EqualsCi("+") || items[1].Trim().EqualsCi("Include"),
                            SourcePattern = items[2].Trim()
                        };
                        obj.RegexSourcePattern = obj.SourcePattern.StartsWith(":") ? obj.SourcePattern.Remove(0, 1) : obj.SourcePattern.Replace('/', '\\').WildCardToRegex();

                        if (!string.IsNullOrEmpty(obj.SourcePattern))
                            list.Add(obj);
                    }
                } catch (Exception e) {
                    outputMessage.AppendLine("Erreur lors de la lecture de la ligne n°" + (lineNb + 1) + " : " + e.Message + "<br>");
                }
            }, Encoding.Default);

            readingErrorsHtml = outputMessage.ToString();

            return list;
        }

        #endregion
    }

    #region DeployRule

    public abstract class DeployRule {
        /// <summary>
        /// Step to which the rule applies : 0 = compilation, 1 = deployment of all files, 2+ = extra
        /// </summary>
        public int Step { get; set; }

        /// <summary>
        /// The line from which we read this info, allows to sort by line
        /// </summary>
        public int Line { get; set; }

        /// <summary>
        /// the full file path in which this rule can be found
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Provides a representation for this rule
        /// </summary>
        public string ToStringDescription() {
            return (Source + "|" + Line).ToHtmlLink("Règle ligne " + Line);
        }
    }

    public class DeployVariableRule : DeployRule {
        /// <summary>
        /// the name of the variable, format &lt;XXX&gt;
        /// </summary>
        public string VariableName { get; set; }

        /// <summary>
        /// The path that should replace the variable &lt;XXX&gt;
        /// </summary>
        public string Path { get; set; }
    }

    public class DeployFilterRule : DeployRule {
        /// <summary>
        /// true if the rule is about including a file (+) false if about excluding (-)
        /// </summary>
        public bool Include { get; set; }

        /// <summary>
        /// Pattern to match in the source path
        /// </summary>
        public string SourcePattern { get; set; }

        /// <summary>
        /// Pattern to match in the source (as a regular expression)
        /// </summary>
        public string RegexSourcePattern { get; set; }
    }

    #endregion

    #region DeployTransferRule

    /// <summary>
    /// Base class for transfer rules
    /// </summary>
    public abstract class DeployTransferRule : DeployRule {

        #region Properties

        /// <summary>
        /// The type of transfer that should occur for this compilation path
        /// </summary>
        public virtual DeployType Type {
            get { return DeployType.Copy; }
        }

        /// <summary>
        /// A transfer can either apply to a file or to a folder
        /// </summary>
        public virtual DeployTransferRuleTarget TargetType {
            get { return DeployTransferRuleTarget.File; }
        }

        /// <summary>
        /// if false, this should be the last rule applied to this file
        /// </summary>
        public bool ContinueAfterThisRule { get; set; }

        /// <summary>
        /// Pattern to match in the source path
        /// </summary>
        public string SourcePattern { get; set; }

        /// <summary>
        /// deploy target depending on the deploy type of this rule
        /// </summary>
        public string DeployTarget { get; set; }

        /// <summary>
        /// True if the rule is directly written as a regex and we want to replace matches in the source directory in the deploy target (in that case it must start with ":")
        /// </summary>
        public bool ShouldDeployTargetReplaceDollar { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Should return true if the rule is valid
        /// </summary>
        /// <param name="error"></param>
        public virtual bool IsValid(out string error) {
            error = null;
            if (!string.IsNullOrEmpty(SourcePattern) && !string.IsNullOrEmpty(DeployTarget)) {
                return true;
            }
            error = ToStringDescription() + " : Le chemin source ou cible est vide";
            return false;
        }

        /// <summary>
        /// Get a copy of this object
        /// </summary>
        /// <returns></returns>
        public virtual DeployTransferRule GetCopy() {
            var theCopy = New(Type);
            theCopy.Source = Source;
            theCopy.Line = Line;
            theCopy.Step = Step;
            theCopy.ContinueAfterThisRule = ContinueAfterThisRule;
            theCopy.SourcePattern = SourcePattern;
            return theCopy;
        }

        #endregion

        #region Factory

        public static DeployTransferRule New(DeployType type) {
            switch (type) {
                case DeployType.Prolib:
                    return new DeployTransferRuleProlib();
                case DeployType.Cab:
                    return new DeployTransferRuleCab();
                case DeployType.Zip:
                    return new DeployTransferRuleZip();
                case DeployType.DeleteInProlib:
                    return new DeployTransferRuleDeleteInProlib();
                case DeployType.Ftp:
                    return new DeployTransferRuleFtp();
                case DeployType.Delete:
                    return new DeployTransferRuleDelete();
                case DeployType.Copy:
                    return new DeployTransferRuleCopy();
                case DeployType.Move:
                    return new DeployTransferRuleMove();
                case DeployType.CopyFolder:
                    return new DeployTransferRuleCopyFolder();
                case DeployType.DeleteFolder:
                    return new DeployTransferRuleDeleteFolder();
                default:
                    throw new ArgumentOutOfRangeException("type", type, null);
            }
        }

        #endregion

        #region GetDeletetionType

        /// <summary>
        /// Returns the type of deployment needed to delete a file deployed with the given type
        /// </summary>
        public static DeployType GetDeletetionType(DeployType type) {
            switch (type) {
                case DeployType.Prolib:
                    return DeployType.DeleteInProlib;
                case DeployType.CopyFolder:
                    return DeployType.DeleteFolder;
                case DeployType.Copy:
                case DeployType.Move:
                    return DeployType.Delete;
                default:
                    return DeployType.None;
            }
        }

        #endregion
    }

    #region DeployTransferRuleDelete

    /// <summary>
    /// Delete file(s) 
    /// </summary>
    public class DeployTransferRuleDelete : DeployTransferRule {
        public override DeployType Type {
            get { return DeployType.Delete; }
        }

        public override bool IsValid(out string error) {
            error = null;
            if (string.IsNullOrEmpty(SourcePattern)) {
                error = ToStringDescription() + " : Le chemin source est vide";
                return false;
            }
            if (Step < 2) {
                error = ToStringDescription() + " : La règle de suppression ne peut s'appliquer que pour les étapes >= 1";
                return false;
            }
            return true;
        }
    }

    #endregion

    #region DeployTransferRuleDeleteFolder

    /// <summary>
    /// Delete folder(s) recursively
    /// </summary>
    public class DeployTransferRuleDeleteFolder : DeployTransferRule {
        public override DeployType Type {
            get { return DeployType.DeleteFolder; }
        }

        public override DeployTransferRuleTarget TargetType {
            get { return DeployTransferRuleTarget.Folder; }
        }

        public override bool IsValid(out string error) {
            error = null;
            if (string.IsNullOrEmpty(SourcePattern)) {
                error = ToStringDescription() + " : Le chemin source est vide";
                return false;
            }
            if (Step < 2) {
                error = ToStringDescription() + " : La règle de suppression ne peut s'appliquer que pour les étapes >= 1";
                return false;
            }
            return true;
        }
    }

    #endregion

    #region DeployTransferRulePack

    #region DeployTransferRulePack

    /// <summary>
    /// Abstract class for PACK rules
    /// </summary>
    public abstract class DeployTransferRulePack : DeployTransferRule {
        public virtual string ArchiveExt {
            get { return ".arc"; }
        }

        public override bool IsValid(out string error) {
            if (!string.IsNullOrEmpty(DeployTarget) && !DeployTarget.ContainsFast(ArchiveExt)) {
                error = ToStringDescription() + " : Le chemin cible devrait pointer vers un fichier avec l'extension " + ArchiveExt;
                return false;
            }
            return base.IsValid(out error);
        }
    }

    #endregion

    #region DeployTransferRuleProlib

    /// <summary>
    /// Transfer file(s) to a .pl file
    /// </summary>
    public class DeployTransferRuleProlib : DeployTransferRulePack {
        public override DeployType Type {
            get { return DeployType.Prolib; }
        }

        public override string ArchiveExt {
            get { return ".pl"; }
        }
    }

    #endregion

    #region DeployTransferRuleZip

    /// <summary>
    /// Transfer file(s) to a .zip file
    /// </summary>
    public class DeployTransferRuleZip : DeployTransferRulePack {
        public override DeployType Type {
            get { return DeployType.Zip; }
        }

        public override string ArchiveExt {
            get { return ".zip"; }
        }
    }

    #endregion

    #region DeployTransferRuleCab

    /// <summary>
    /// Transfer file(s) to a .cab file
    /// </summary>
    public class DeployTransferRuleCab : DeployTransferRulePack {
        public override DeployType Type {
            get { return DeployType.Cab; }
        }

        public override string ArchiveExt {
            get { return ".cab"; }
        }
    }

    #endregion

    #region DeployTransferRuleDeleteInProlib

    /// <summary>
    /// Delete file(s) in a prolib file
    /// </summary>
    public class DeployTransferRuleDeleteInProlib : DeployTransferRulePack {
        public override DeployType Type {
            get { return DeployType.DeleteInProlib; }
        }

        public override string ArchiveExt {
            get { return ".pl"; }
        }

        public override bool IsValid(out string error) {
            error = null;
            if (string.IsNullOrEmpty(SourcePattern) || string.IsNullOrEmpty(DeployTarget)) {
                error = ToStringDescription() + " : The chemin vers la .pl ou bien le chemin relatif dans la .pl est vide";
                return false;
            }
            if (Step < 2) {
                error = ToStringDescription() + " : La règle de suppression ne peut s'appliquer que pour les étapes >= 1";
                return false;
            }
            if (!SourcePattern.EndsWith(ArchiveExt)) {
                error = ToStringDescription() + " : Le chemin source devrait finir par l'extension " + ArchiveExt;
                return false;
            }
            return true;
        }
    }

    #endregion

    #region DeployTransferRuleFtp

    /// <summary>
    /// Send file(s) over FTP
    /// </summary>
    public class DeployTransferRuleFtp : DeployTransferRulePack {
        public override DeployType Type {
            get { return DeployType.Ftp; }
        }

        public override bool IsValid(out string error) {
            if (!string.IsNullOrEmpty(DeployTarget) && !DeployTarget.IsValidFtpAdress()) {
                error = ToStringDescription() + " : La cible devrait avoir le format ftp://user:pass@server:port/distantpath/ (avec user/pass/port en option)";
                return false;
            }
            return base.IsValid(out error);
        }
    }

    #endregion

    #endregion

    #region DeployTransferRuleCopyFolder

    /// <summary>
    /// Copy folder(s) recursively
    /// </summary>
    public class DeployTransferRuleCopyFolder : DeployTransferRule {
        public override DeployType Type {
            get { return DeployType.CopyFolder; }
        }

        public override DeployTransferRuleTarget TargetType {
            get { return DeployTransferRuleTarget.Folder; }
        }

        public override bool IsValid(out string error) {
            if (Step < 2) {
                error = ToStringDescription() + " : La copie de dossiers ne peut s'appliquer que pour les étapes >= 1";
                return false;
            }
            return base.IsValid(out error);
        }
    }

    #endregion

    #region DeployTransferRuleCopy

    /// <summary>
    /// Copy file(s) 
    /// </summary>
    public class DeployTransferRuleCopy : DeployTransferRule {
        public override DeployType Type {
            get { return DeployType.Copy; }
        }
    }

    #endregion

    #region DeployTransferRuleMove

    /// <summary>
    /// Move file(s) 
    /// </summary>
    public class DeployTransferRuleMove : DeployTransferRule {
        public override DeployType Type {
            get { return DeployType.Move; }
        }
    }

    #endregion

    #endregion

    #region DeployTransferRuleTarget

    /// <summary>
    /// Types of deploy, used during rules sorting
    /// </summary>
    public enum DeployTransferRuleTarget : byte {
        File = 1,
        Folder = 2
    }

    #endregion

    #region DeployType

    /// <summary>
    /// Types of deploy, used during rules sorting
    /// </summary>
    public enum DeployType : byte {
        None = 0,
        Delete = 1,
        DeleteFolder = 2,

        DeleteInProlib = 10,
        Prolib = 11,
        Zip = 12,
        Cab = 13,
        Ftp = 14,
        // every item above are treated in "packs"

        CopyFolder = 21,

        // Copy / move should always be last
        Copy = 30,
        Move = 31
    }

    #endregion
}