namespace abldeployer.Core.Execution {
    internal class ProExecutionGenerateDebugfile : ProExecutionHandleCompilation {

        public override ExecutionType ExecutionType { get { return ExecutionType.GenerateDebugfile; } }

        public string GeneratedFilePath {
            get {
                if (CompileWithListing)
                    return Files.First().CompOutputLis;
                if (CompileWithXref)
                    return Files.First().CompOutputXrf;
                return Files.First().CompOutputDbg;
            }
        }

        public ProExecutionGenerateDebugfile() {
            CompileWithDebugList = false;
            CompileWithXref = false;
            CompileWithListing = false;
            UseXmlXref = false;
        }

        protected override bool CanUseBatchMode() {
            return true;
        }

    }
}