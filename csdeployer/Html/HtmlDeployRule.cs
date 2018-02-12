using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using abldeployer.Core;
using csdeployer.Lib;

namespace csdeployer.Html {

    internal static class HtmlDeployRule {

        public static string Description(string source, int line) {
            return (source + "|" + line).ToHtmlLink("Règle ligne " + line);
        }

        public static string Description(DeployRule rule) {
            return Description(rule.Source, rule.Line);
        }
    }
}
