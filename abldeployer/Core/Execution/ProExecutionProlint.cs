using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using abldeployer.Lib;

namespace abldeployer.Core.Execution {
    internal class ProExecutionProlint : ProExecutionHandleCompilation {

        public override ExecutionType ExecutionType { get { return ExecutionType.Prolint; } }

        private string _prolintOutputPath;

        protected override string CheckParameters() {

            // Check if the startprolint procedure exists or create it from resources
            if (!File.Exists(Config.ProlintStartProcedure))
                if (!Utils.FileWriteAllBytes(Config.ProlintStartProcedure, DataResources.StartProlint))
                    return "Could not write the prolint entry point procedure, check reading rights for the file : " + Config.ProlintStartProcedure.ToHtmlLink();

            return base.CheckParameters();
        }

        protected override bool SetExecutionInfo() {

            if (!base.SetExecutionInfo())
                return false;

            if (!Config.Instance.GlobalDontCheckProlintUpdates && (!Updater<ProlintUpdaterWrapper>.Instance.LocalVersion.IsHigherVersionThan("v0") || !Updater<ProparseUpdaterWrapper>.Instance.LocalVersion.IsHigherVersionThan("v0"))) {
                UserCommunication.NotifyUnique("NeedProlint", 
                    "The Prolint installation folder could not be found in 3P.<br>This is normal if it is the first time that you are using this feature.<br><br>" + "download".ToHtmlLink("Please click here to download the latest release of Prolint automatically") + "<br><br><i>You will be informed when it is installed and you will be able to use this feature immediately after.<br><br>If you do not wish to download it and see this message again :<br> toggle off automatic updates for Prolint in the " + "options".ToHtmlLink("update options page") + ".<br>Please note that in that case, you will need to configure Prolint yourself</i>", 
                    MessageImg.MsgQuestion, "Prolint execution", "Prolint installation not found", args => {
                        if (args.Link.Equals("options")) {
                            args.Handled = true;
                            Appli.Appli.GoToPage(PageNames.OptionsUpdate);
                        } else if (args.Link.Equals("download")) {
                            args.Handled = true;
                            Updater<ProlintUpdaterWrapper>.Instance.CheckForUpdate();
                            Updater<ProparseUpdaterWrapper>.Instance.CheckForUpdate();
                        }
                        if (args.Handled)
                            UserCommunication.CloseUniqueNotif("NeedProlint");
                    });
                return false;
            }

            // prolint, we need to copy the StartProlint program
            var fileToExecute = "prolint_" + DateTime.Now.ToString("yyMMdd_HHmmssfff") + ".p";
            _prolintOutputPath = Path.Combine(_localTempDir, "prolint.log");

            StringBuilder prolintProgram = new StringBuilder();
            prolintProgram.AppendLine("&SCOPED-DEFINE PathFileToProlint " + Files.First().CompiledSourcePath.PreProcQuoter());
            prolintProgram.AppendLine("&SCOPED-DEFINE PathProlintOutputFile " + _prolintOutputPath.PreProcQuoter());
            prolintProgram.AppendLine("&SCOPED-DEFINE PathToStartProlintProgram " + Config.ProlintStartProcedure.PreProcQuoter());
            prolintProgram.AppendLine("&SCOPED-DEFINE UserName " + Config.Instance.UserName.PreProcQuoter());
            prolintProgram.AppendLine("&SCOPED-DEFINE PathActualFilePath " + Files.First().SourcePath.PreProcQuoter());
            var filename = Npp.CurrentFileInfo.FileName;
            if (FileCustomInfo.Contains(filename)) {
                var fileInfo = FileCustomInfo.GetLastFileTag(filename);
                prolintProgram.AppendLine("&SCOPED-DEFINE FileApplicationName " + fileInfo.ApplicationName.PreProcQuoter());
                prolintProgram.AppendLine("&SCOPED-DEFINE FileApplicationVersion " + fileInfo.ApplicationVersion.PreProcQuoter());
                prolintProgram.AppendLine("&SCOPED-DEFINE FileWorkPackage " + fileInfo.WorkPackage.PreProcQuoter());
                prolintProgram.AppendLine("&SCOPED-DEFINE FileBugID " + fileInfo.BugId.PreProcQuoter());
                prolintProgram.AppendLine("&SCOPED-DEFINE FileCorrectionNumber " + fileInfo.CorrectionNumber.PreProcQuoter());
                prolintProgram.AppendLine("&SCOPED-DEFINE FileDate " + fileInfo.CorrectionDate.PreProcQuoter());

                prolintProgram.AppendLine("&SCOPED-DEFINE ModificationTagOpening " + ModificationTag.ReplaceTokens(fileInfo, ModificationTagTemplate.Instance.TagOpener).PreProcQuoter());
                prolintProgram.AppendLine("&SCOPED-DEFINE ModificationTagEnding " + ModificationTag.ReplaceTokens(fileInfo, ModificationTagTemplate.Instance.TagCloser).PreProcQuoter());
            }
            prolintProgram.AppendLine("&SCOPED-DEFINE PathDirectoryToProlint " + Updater<ProlintUpdaterWrapper>.Instance.ApplicationFolder.PreProcQuoter());
            prolintProgram.AppendLine("&SCOPED-DEFINE PathDirectoryToProparseAssemblies " + Updater<ProparseUpdaterWrapper>.Instance.ApplicationFolder.PreProcQuoter());
            var encoding = TextEncodingDetect.GetFileEncoding(Config.ProlintStartProcedure);
            Utils.FileWriteAllText(Path.Combine(_localTempDir, fileToExecute), Utils.ReadAllText(Config.ProlintStartProcedure, encoding).Replace(@"/*<inserted_3P_values>*/", prolintProgram.ToString()), encoding);

            SetPreprocessedVar("CurrentFilePath", fileToExecute.PreProcQuoter());

            return true;
        }

        protected override Dictionary<string, List<FileError>> GetErrorsList(Dictionary<string, string> changePaths) {
            return ReadErrorsFromFile(_prolintOutputPath, true, changePaths);
        }
    }
}