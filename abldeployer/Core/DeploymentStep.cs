namespace abldeployer.Core {
    public enum DeploymentStep {
        CopyingReference,
        Listing,
        Compilation,
        DeployRCode,
        DeployFile,
        CopyingFinalPackageToDistant,
        BuildingWebclientDiffs,
        BuildingWebclientCompleteCab
    }
}