namespace abldeployer.Core.Execution {
    internal class ProExecutionCheckSyntax : ProExecutionHandleCompilation {
        public override ExecutionType ExecutionType { get { return ExecutionType.CheckSyntax; } }

        protected override bool CanUseBatchMode() {
            return true;
        }
    }
}