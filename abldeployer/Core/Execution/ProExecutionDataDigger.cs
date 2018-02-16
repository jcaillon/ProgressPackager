using System.Text;
using abldeployer.Lib;

namespace abldeployer.Core.Execution {
    internal class ProExecutionDataDigger : ProExecution {
        public override ExecutionType ExecutionType { get { return ExecutionType.DataDigger; } }

        protected override bool SetExecutionInfo() {

            if (!Updater<DataDiggerUpdaterWrapper>.Instance.LocalVersion.IsHigherVersionThan("v0")) {
                UserCommunication.NotifyUnique("NeedDataDigger",
                    "The DataDigger installation folder could not be found in 3P.<br>This is normal if it is the first time that you are using this feature.<br><br>" + "download".ToHtmlLink("Please click here to download the latest release of DataDigger automatically") + "<br><br><i>You will be informed when it is installed and you will be able to use this feature immediately after.</i>",
                    MessageImg.MsgQuestion, "DataDigger execution", "DataDigger installation not found", args => {
                        if (args.Link.Equals("download")) {
                            args.Handled = true;
                            Updater<DataDiggerUpdaterWrapper>.Instance.CheckForUpdate();
                            UserCommunication.CloseUniqueNotif("NeedDataDigger");
                        }
                    });
                return false;
            }

            // add the datadigger folder to the propath
            _propath = Config.DataDiggerFolder + "," + _propath;
            _processStartDir = Config.DataDiggerFolder;

            return true;
        }

        protected override void AppendProgressParameters(StringBuilder sb) {
            sb.Append(" -basekey \"INI\" -s 10000 -d dmy -E -rereadnolock -h 255 -Bt 4000 -tmpbsize 8 ");
            sb.Append(" -T " + _localTempDir.Trim('\\').Quoter());
        }
    }
}