#region header
// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (ProgressForm.cs) is part of csdeployer.
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
using System.ComponentModel;
using System.Windows.Forms;
using csdeployer.Lib;

namespace csdeployer.Form {
    internal partial class ProgressForm : System.Windows.Forms.Form {
        #region Properties

        public ProgressTreatment MainTreatment { get; private set; }

        #endregion

        #region Life and death

        /// <summary>
        /// Constructor
        /// </summary>
        public ProgressForm(ProgressTreatment mainTreatment) {
            InitializeComponent();
            Tag = false;
            bar1.Minimum = 0;
            bar1.Maximum = 100;
            bar2.Minimum = 0;
            bar2.Maximum = 100;

            MainTreatment = mainTreatment;
            MainTreatment.OnProgress += OnProgress;
            MainTreatment.OnStop += OnStop;
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Call this method instead of Close() to really close this form
        /// </summary>
        public void ForceClose() {
            Tag = true;
            this.SafeInvoke(form => form.Close());
        }

        #endregion

        #region Private methods

        private bool ConfirmCancel() {
            var answer = MessageBox.Show(@"souhaitez-vous annuler le traitement en cours?", @"Annuler traitement", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2);
            return answer == DialogResult.Yes;
        }

        #endregion

        #region Event handlers

        /// <summary>
        /// The treatment stops
        /// </summary>
        private void OnStop(object sender, EventArgs e) {
            ForceClose();
        }

        /// <summary>
        /// Called when the treatment is progressing
        /// </summary>
        private void OnProgress(object sender, ProgressionEventArgs e) {
            this.SafeInvoke(form => {
                bar1.Value = (int) e.GlobalProgression;
                bar1.Text = Math.Round(e.GlobalProgression, 1) + @"%";

                bar2.Value = (int) e.CurrentStepProgression;
                bar2.Text = Math.Round(e.CurrentStepProgression, 1) + @"%";

                lblCurrentStep.Text = e.CurrentStepName;
                lblElapsed.Text = @"Temps total écoulé " + e.ElpasedTime;
            });
        }

        /// <summary>
        /// Called when the form is shown
        /// </summary>
        /// <param name="e"></param>
        protected override void OnShown(EventArgs e) {
            if (!string.IsNullOrEmpty(Start.Title))
                Text = Start.Title;

            base.OnShown(e);
            MainTreatment.Start();
            MaximumSize = Size;
            MinimumSize = Size;
        }

        /// <summary>
        /// Click on cancel
        /// </summary>
        private void OnbtCancelClick(object sender, EventArgs e) {
            Close();
        }

        /// <summary>
        /// Before closing the form
        /// </summary>
        protected override void OnClosing(CancelEventArgs e) {
            if ((bool) Tag)
                return;
            if (!ConfirmCancel()) {
                e.Cancel = true;
            } else {
                Tag = true;
                MainTreatment.Cancel();
            }
            base.OnClosing(e);
        }

        #endregion
    }
}