namespace abldeployer.Core.Execution {
    internal class ProExecutionDbAdmin : ProExecution {
        public override ExecutionType ExecutionType { get { return ExecutionType.DbAdmin; } }
    }
}