using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace abldeployer.Core.Config {
    
    public class ConfigExecutionMultiCompilation : ConfigExecutionCompilation {
        
        public ConfigExecutionMultiCompilation() {
            NumberProcessPerCore = 1;
        }

        public bool ForceSingleProcess { get; set; }
        public bool OnlyGenerateRcode { get; set; }
        public int NumberProcessPerCore { get; set; }
        public bool CompileForceUseOfTemp { get; set; }

    }
}
