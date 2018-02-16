namespace abldeployer.Core.Config {
    public interface IConfigExecutionDatabase : IConfigExecution {
        string DatabaseExtractCandoTblType { get; set; }
        string DatabaseExtractCandoTblName { get; set; }
        byte[] ProgramDumpTableCrc { get; }
    }
}