using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using abldeployer.Core;
using csdeployer.Lib;

namespace csdeployer.Html {

    internal static class HtmlFileError {

        public static string Description(FileError error) {
            var sb = new StringBuilder();
            sb.Append("<div>");
            sb.Append("<img height='15px' src='");
            sb.Append(error.Level > ErrorLevel.StrongWarning ? "Error_25x25" : "Warning_25x25");
            sb.Append("'>");
            if (!error.CompiledFilePath.Equals(error.SourcePath)) {
                sb.Append("in ");
                sb.Append(error.SourcePath.ToHtmlLink(Path.GetFileName(error.SourcePath)));
                sb.Append(", ");
            }
            sb.Append((error.SourcePath + "|" + error.Line).ToHtmlLink("Ligne " + (error.Line + 1)));
            sb.Append(" (erreur n°" + error.ErrorNumber + ")");
            if (error.Times > 0) {
                sb.Append(" (x" + error.Times + ")");
            }
            sb.Append(" " + System.Security.SecurityElement.Escape(error.Message));
            sb.Append("</div>");
            return sb.ToString();
        }
    }
}
