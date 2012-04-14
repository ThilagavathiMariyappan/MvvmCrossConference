using System;
using System.Net;
using Cirrious.Conference.Core.Interfaces;
using Cirrious.Conference.Core.Models.Raw;
using Cirrious.MvvmCross.Commands;
using Cirrious.MvvmCross.ExtensionMethods;
using Cirrious.MvvmCross.Interfaces.Commands;
using Cirrious.MvvmCross.Interfaces.Platform;
using Cirrious.MvvmCross.Interfaces.ServiceProvider;
using Newtonsoft.Json;

namespace Cirrious.Conference.Core.ViewModels
{
    public class UpdateViewModel
        : BaseViewModel
        , IMvxServiceConsumer<IMvxSimpleFileStoreService>
        , IMvxServiceConsumer<IApplicationSettings>
        , IMvxServiceConsumer<IConferenceStart>
    {
        private bool _hasStartedRealApp;

        private bool _isFetching;

        public bool IsFetching
        {
            get { return _isFetching; }
            set
            {
                _isFetching = value;
                this.FirePropertyChanged("IsFetching");
            }
        }

        private bool _isUpdating;

        public bool IsUpdating
        {
            get { return _isUpdating; }
            set
            {
                _isUpdating = value;
                this.FirePropertyChanged("IsUpdating");
            }
        }

        private Exception _error;

        public Exception Error
        {
            get { return _error; }
            set
            {
                _error = value;
                this.FirePropertyChanged("Error");
            }
        }

        private bool _fileAvailable;

        public bool FileAvailable
        {
            get { return _fileAvailable; }
            set
            {
                _fileAvailable = value;
                this.FirePropertyChanged("FileAvailable");
            }
        }

        public UpdateViewModel()
        {
            BeginDownload();
        }

        public IMvxCommand SkipCommand
        {
            get { return new MvxRelayCommand(StartRealAppNow); }
        }

        private void StartRealAppNow()
        {
            _hasStartedRealApp = true;
            this.GetService<IConferenceStart>().StartApp();
        }

        private void DoUpdate()
        {
            if (_hasStartedRealApp)
            {
                Trace.Warn("Can't update after a skip");
                return;
            }

            var fileService = this.GetService<IMvxSimpleFileStoreService>();
            if (!fileService.TryMove(Constants.TempSessionsFileName, Constants.SessionsFileName, true))
            {
                ReportError(new Exception(TextSource.GetText("UnableToCopyFile")));
                return;
            }

            // update is complete... so now let's update the app settings and start the app
            this.GetService<IApplicationSettings>().DataLastUpdatedUtc = DateTime.UtcNow;
            this.GetService<IConferenceStart>().StartApp();
        }

        private void BeginDownload()
        {
            if (IsFetching)
            {
                Trace.Warn("Already getting new remote data for conference");
                return;
            }

            if (FileAvailable)
            {
                Trace.Warn("File already available");
                return;
            }

            Trace.Info("Get remote data for conference");
            var webClient = new WebClient();

            IsFetching = true;

            webClient.UploadStringCompleted += (sender, e) => HandleStringDownloadComplete(e);
            webClient.Encoding = System.Text.Encoding.UTF8;
            webClient.UploadStringAsync(new Uri(Constants.SessionDataEndPoint), "POST", string.Empty);
        }

        private void ReportError(Exception error)
        {
            Error = error;

            if (_hasStartedRealApp)
                return;

            // move on, then report the error - order is a bit naughty here, but will hopefully work on most platforms...
            // alternative is to design a flow in each of he view implementations (e.g. show toast, then automatically call skipcommand from them)
            StartRealAppNow();
            ReportError(this.TextSource.GetText("ErrorDuringUpdate", error.Message));
        }

        private void HandleStringDownloadComplete(UploadStringCompletedEventArgs e)
        {
            try
            {
                if (_hasStartedRealApp)
                {
                    // nothing to do here - user has skipped the step
                    return;
                }

                if (e.Error != null)
                {
                    ReportError(e.Error);
                    return;
                }

                var r = e.Result;
                Trace.Info("Start deserialising conference data");
                var conferenceModel = JsonConvert.DeserializeObject<PocketConferenceModel>(r);
                Trace.Info("Completed deserialising conference data");

                // could test more valid conditions here?
                if (conferenceModel == null
                    || conferenceModel.Sessions == null
                    || conferenceModel.Sessions.Count == 0)
                {
                    throw new InvalidOperationException("Downloaded model smells wrong");
                }

                var service = this.GetService<IMvxSimpleFileStoreService>();
                service.WriteFile(Constants.TempSessionsFileName, e.Result);

                FileAvailable = true;

                if (!_hasStartedRealApp)
                {
                    this.InvokeOnMainThread(DoUpdate);
                }
            }
            catch (Exception ex)
            {
                Trace.Error(
                    "Problem with downloaded conference data: {0}",
                    ex.ToLongString());
                ReportError(ex);
            }
            finally
            {
                IsFetching = false;
            }
        }
    }
}