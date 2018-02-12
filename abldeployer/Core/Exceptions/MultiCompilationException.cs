using System;

namespace abldeployer.Core.Exceptions {
    public class MultiCompilationException : Exception {
        public MultiCompilationException() { }
        public MultiCompilationException(string message) : base(message) { }
        public MultiCompilationException(string message, Exception innerException) : base(message, innerException) { }
    }
}