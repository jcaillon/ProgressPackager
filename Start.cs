#region header
// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (Start.cs) is part of csdeployer.
// 
// csdeployer is a free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// csdeployer is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with csdeployer. If not, see <http://www.gnu.org/licenses/>.
// ========================================================================
#endregion
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using csdeployer.Core;
using csdeployer.Form;

namespace csdeployer {
    internal static class Start {

        #region Main

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static void Main() {

            // catch unhandled errors to log them
            AppDomain.CurrentDomain.UnhandledException += ErrorHandler.UnhandledErrorHandler;
            Application.ThreadException += ErrorHandler.ThreadErrorHandler;
            TaskScheduler.UnobservedTaskException += ErrorHandler.UnobservedErrorHandler;

            var args = Environment.GetCommandLineArgs();
            if (args.Length >= 2) {
                XmlConfigPath = args[1];
            }
            if (string.IsNullOrEmpty(XmlConfigPath) || !File.Exists(XmlConfigPath)) {
                MessageBox.Show(@"Impossible de trouver le fichier de configuration, ce programme va maintenant se terminer", @"Config xml manquant", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (args.Length >= 3) {
                Title = args[2];
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ProgressForm(new MainTreatment()));
        }

        #endregion

        #region Properties

        /// <summary>
        /// title of the window
        /// </summary>
        internal static string Title { get; set; }
        
        /// <summary>
        /// Path to the input config file 
        /// </summary>
        internal static string XmlConfigPath { get; set; }

        #endregion
    }
}