using System.Collections.Generic;
using System.IO;
using System.Linq;
using abldeployer.Core.Config;
using abldeployer.Core.Exceptions;
using abldeployer.Lib;

namespace abldeployer.Core.Execution {
    public class ProExecutionCompile : ProExecutionHandleCompilation {
        #region Life and death

        /// <summary>
        ///     Construct with the current env
        /// </summary>
        public ProExecutionCompile() : this(null) { }

        public ProExecutionCompile(ConfigExecutionCompilation proEnv) : base(proEnv) { }

        #endregion

        #region Override

        public override ExecutionType ExecutionType {
            get { return ExecutionType.Compile; }
        }

        /// <summary>
        ///     Creates a list of files to deploy after a compilation,
        ///     for each Origin file will correspond one (or more if it's a .cls) .r file,
        ///     and one .lst if the option has been checked
        /// </summary>
        protected override List<FileToDeploy> GetFilesToDeployAfterCompilation() {
            return Deployer.GetFilesToDeployAfterCompilation(this);
        }

        protected override void CheckParameters() {
            if (!ProEnv.CompileLocally && !Path.IsPathRooted(ProEnv.TargetDirectory)) throw new ExecutionParametersException("The compilation is not done near the source and the target directory is invalid : " + (string.IsNullOrEmpty(ProEnv.TargetDirectory) ? "it's empty!" : ProEnv.TargetDirectory).Quoter());
            base.CheckParameters();
        }

        protected override bool CanUseBatchMode() {
            return true;
        }

        /// <summary>
        ///     get the output directory that will be use to generate the .r (and listing debug-list...)
        /// </summary>
        protected override void ComputeOutputDir(FileToCompile fileToCompile, string localSubTempDir, int count) {
            // for *.cls files, as many *.r files are generated, we need to compile in a temp directory
            // we need to know which *.r files were generated for each input file
            // so each file gets his own sub tempDir
            var lastDeployment = ProEnv.Deployer.GetTransfersNeededForFile(fileToCompile.SourcePath, 0).Last();
            if (lastDeployment.DeployType != DeployType.Move ||
                ProEnv.CompileForceUseOfTemp ||
                Path.GetExtension(fileToCompile.SourcePath ?? "").Equals(ExtCls))
                if (lastDeployment.DeployType != DeployType.Ftp &&
                    !string.IsNullOrEmpty(ProEnv.TargetDirectory) && ProEnv.TargetDirectory.Length > 2 && !ProEnv.TargetDirectory.Substring(0, 2).EqualsCi(_localTempDir.Substring(0, 2))) {
                    if (!Directory.Exists(DistantTempDir)) {
                        var dirInfo = Directory.CreateDirectory(DistantTempDir);
                        dirInfo.Attributes |= FileAttributes.Hidden;
                    }
                    fileToCompile.CompilationOutputDir = Path.Combine(DistantTempDir, count.ToString());
                } else {
                    fileToCompile.CompilationOutputDir = localSubTempDir;
                }
            else fileToCompile.CompilationOutputDir = lastDeployment.TargetBasePath;
        }

        #endregion
    }
}