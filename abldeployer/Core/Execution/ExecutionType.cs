namespace abldeployer.Core.Execution {

    public enum ExecutionType {
        CheckSyntax = 0,
        Compile = 1,
        Run = 2,
        GenerateDebugfile = 3,
        Prolint = 4,

        Database = 10,
        Appbuilder = 11,
        Dictionary = 12,
        DataDigger = 13,
        DataReader = 14,
        DbAdmin = 15,
        ProDesktop = 16,
        DeploymentHook = 17,
        ProVersion = 18,
        TableCrc = 19,
    }

}