#region header
// ========================================================================
// Copyright (c) 2017 - Julien Caillon (julien.caillon@gmail.com)
// This file (ProgressTreatment.cs) is part of csdeployer.
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
using System.Windows.Forms;

namespace csdeployer.Form {

    #region ProgressTreatment

    /// <summary>
    /// Class for that handles the deployment done in this app
    /// </summary>
    internal abstract class ProgressTreatment {
        #region Events

        /// <summary>
        /// Published when the treatment progresses
        /// </summary>
        public EventHandler<ProgressionEventArgs> OnProgress;

        /// <summary>
        /// Published when the treatment has ended
        /// </summary>
        public EventHandler<EventArgs> OnStop;

        #endregion

        #region Private

        private Timer _progressTimer;

        #endregion

        #region Public methods (called by the progress form)

        /// <summary>
        /// Start of the treatment, called when the progress form is shown
        /// </summary>
        public void Start() {
            // start the timer
            _progressTimer = new Timer {
                Interval = 500
            };
            _progressTimer.Tick += (o, args) => UpdateProgress();
            _progressTimer.Start();

            StartJob();
        }

        public virtual void Cancel() {
            Stop();
        }

        #endregion

        #region To override

        /// <summary>
        /// Should return the progression of the treatment
        /// </summary>
        /// <returns></returns>
        protected virtual ProgressionEventArgs GetProgress() {
            return null;
        }

        /// <summary>
        /// Called when the treatment starts
        /// </summary>
        protected virtual void StartJob() { }

        /// <summary>
        /// Called when the treatment stops
        /// </summary>
        protected virtual void StopJob() { }

        #endregion

        #region Private

        /// <summary>
        /// Called every now and then to update the progression of the treatment
        /// </summary>
        private void UpdateProgress() {
            if (OnProgress != null) {
                var progress = GetProgress();
                if (progress != null)
                    OnProgress(this, progress);
            }
        }

        /// <summary>
        /// When the treatment ends (call it to stop the treatment)
        /// </summary>
        protected void Stop() {
            // get rid of the timer
            if (_progressTimer != null) {
                _progressTimer.Stop();
                _progressTimer.Dispose();
                _progressTimer = null;
            }

            StopJob();

            // close the interface
            if (OnStop != null) {
                OnStop(this, null);
            }
        }

        #endregion
    }

    #endregion

    #region ProgressionEventArgs

    internal class ProgressionEventArgs : EventArgs {
        /// <summary>
        /// Overall progression of the treatment
        /// </summary>
        public float GlobalProgression { get; set; }

        /// <summary>
        /// Current step
        /// </summary>
        public string CurrentStepName { get; set; }

        /// <summary>
        /// Current step %
        /// </summary>
        public float CurrentStepProgression { get; set; }

        /// <summary>
        /// Time since the start
        /// </summary>
        public string ElpasedTime { get; set; }
    }

    #endregion
}