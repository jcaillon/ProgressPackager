using System.IO;
using System.Text;
using abldeployer.Core.Config;
using abldeployer.Lib;

namespace abldeployer.Core.Execution {
    public class ProExecutionProVersion : ProExecution {
        private string _outputPath;

        public override ExecutionType ExecutionType {
            get { return ExecutionType.ProVersion; }
        }

        public string ProVersion {
            get { return Utils.ReadAllText(_outputPath, Encoding.Default); }
        }

        public ProExecutionProVersion(ConfigExecution proEnv) : base(proEnv) { }

        protected override void SetExecutionInfo() {
            _outputPath = Path.Combine(_localTempDir, "pro.version");
            SetPreprocessedVar("OutputPath", _outputPath.PreProcQuoter());
        }

        protected override void AppendProgressParameters(StringBuilder sb) {
            sb.Clear();
            _exeParameters.Append(" -b -p " + _runnerPath.Quoter());
        }

        protected override bool CanUseBatchMode() {
            return true;
        }
    }
}