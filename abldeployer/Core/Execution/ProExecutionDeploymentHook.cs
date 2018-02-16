using System;
using System.IO;
using System.Text;
using abldeployer.Core.Config;
using abldeployer.Lib;

namespace abldeployer.Core.Execution {

    public class ProExecutionDeploymentHook : ProExecution {
        public override ExecutionType ExecutionType {
            get { return ExecutionType.DeploymentHook; }
        }

        public string DeploymentSourcePath { get; set; }

        public int DeploymentStep { get; set; }

        public ProExecutionDeploymentHook(ConfigExecution proEnv) : base(proEnv) { }

        protected override void SetExecutionInfo() {
            var fileToExecute = "hook_" + DateTime.Now.ToString("yyMMdd_HHmmssfff") + ".p";
            var hookProc = new StringBuilder();
            hookProc.AppendLine("&SCOPED-DEFINE StepNumber " + DeploymentStep);
            hookProc.AppendLine("&SCOPED-DEFINE SourceDirectory " + DeploymentSourcePath.PreProcQuoter());
            hookProc.AppendLine("&SCOPED-DEFINE DeploymentDirectory " + ProEnv.TargetDirectory.PreProcQuoter());
            var encoding = TextEncodingDetect.GetFileEncoding(ProEnv.FileDeploymentHook);
            File.WriteAllText(Path.Combine(_localTempDir, fileToExecute), Utils.ReadAllText(ProEnv.FileDeploymentHook, encoding).Replace(@"/*<inserted_3P_values>*/", hookProc.ToString()), encoding);

            SetPreprocessedVar("CurrentFilePath", fileToExecute.PreProcQuoter());
        }
    }
}