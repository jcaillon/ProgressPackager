using System.IO;

namespace abldeployer.Core.Execution {
    internal class ProExecutionRun : ProExecutionHandleCompilation {

        public override ExecutionType ExecutionType { get { return ExecutionType.Run; } }

        protected override bool SetExecutionInfo() {

            if (!base.SetExecutionInfo())
                return false;

            _processStartDir = Path.GetDirectoryName(Files.First().SourcePath) ?? _localTempDir;

            return true;
        }

    }
}