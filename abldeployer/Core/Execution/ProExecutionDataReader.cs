namespace abldeployer.Core.Execution {
    internal class ProExecutionDataReader : ProExecutionDataDigger {
        public override ExecutionType ExecutionType { get { return ExecutionType.DataReader; } }
    }
}