using System;

namespace abldeployer.Core.Exceptions {
    public class DeploymentException : Exception {
        public DeploymentException() { }
        public DeploymentException(string message) : base(message) { }
        public DeploymentException(string message, Exception innerException) : base(message, innerException) { }
    }
}