﻿//MIT License

//Copyright (c) 2021-2021 Peter Kirmeier

//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.

using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Xamarin.Forms;
using HitCounterManager.Common;
using HitCounterManager.Models;

namespace HitCounterManager.ViewModels
{
    public class ProfileViewModel : NotifyPropertyChangedImpl
    {
        private SettingsRoot Settings = App.CurrentApp.Settings;
        public Picker ProfileSelector = null;

        public ProfileViewModel()
        {
            string Name = Settings.ProfileSelected; // TODO: Update of this field is missing

            _ProfileList = new ObservableCollection<ProfileModel>();
            foreach (Profile prof in Settings.Profiles.ProfileList)
            {
                ProfileModel profileModel = new ProfileModel(prof);
                profileModel.ProfileDataChanged += OutputDataChangedHandler;
                _ProfileList.Add(profileModel);
            }
            _ProfileList.CollectionChanged += CollectionChangedHandler;

            foreach (ProfileModel prof in _ProfileList)
            {
                if (prof.Name == Name)
                {
                    ProfileSelected = prof;
                    break;
                }
            }
            // when no matching profile is found, we start with the first one
            if (null == ProfileSelected) ProfileSelected = _ProfileList[0];

            CmdRemoveSplit = new Command<ProfileRowModel>(RemoveSplit);
            CmdSetActiveSplit = new Command<ProfileRowModel>(SetActiveSplit);
            CmdSetSessionProgress = new Command<ProfileRowModel>(SetSessionProgress);

            ProfileNew = new Command<string>((string NewName) =>
            {
                if (App.CurrentApp.Settings.ReadOnlyMode) return;

                if (null == NewName) return;
                if (NewName.Length == 0) return;
                if (IsProfileExisting(NewName)) return;

                TimerRunning = false;

                // Create profile
                Profile profile = new Profile();
                profile.Name = NewName;
                ProfileModel profileModel = new ProfileModel(profile);
                profileModel.InsertNewRow();
                profileModel.ProfileDataChanged += OutputDataChangedHandler;

                // Add and select profile
                Settings.Profiles.ProfileList.Add(profile);
                Settings.Profiles.ProfileList.Sort((a, b) => a.Name.CompareTo(b.Name)); // Sort by name
                int profileIndex = Settings.Profiles.ProfileList.IndexOf(profile);
                _ProfileList.Insert(profileIndex, profileModel);
                ProfileSelected = profileModel;

                // TODO: [PICKER] Test and rework code if a ObservableCollection can be used for the picker instead!
                ProfileSelector?.ForceUpdate(); // Workaround as this should not be required!
            });
            ProfileRename = new Command<string>((string NewName) =>
            {
                if (App.CurrentApp.Settings.ReadOnlyMode) return;

                if (null == NewName) return;
                if (NewName.Length == 0) return;
                if (IsProfileExisting(NewName)) return;

                _ProfileSelected.Name = NewName;

                // TODO: [PICKER] Test and rework code if a ObservableCollection can be used for the picker instead!
                ProfileSelector?.ForceUpdate(); // Workaround as this should not be required!
            });
            ProfileCopy = new Command<string>((string NewName) =>
            {
                if (App.CurrentApp.Settings.ReadOnlyMode) return;

                if (null == NewName) return;
                if (NewName.Length == 0) return;
                if (IsProfileExisting(NewName)) return;

                UpdateDuration();

                // Create profile
                Profile profile = ProfileSelected.DeepCopyOrigin();
                profile.Name = NewName;
                ProfileModel profileModel = new ProfileModel(profile);
                profileModel.ProfileDataChanged += OutputDataChangedHandler;

                // Add and select profile
                Settings.Profiles.ProfileList.Add(profile);
                Settings.Profiles.ProfileList.Sort((a, b) => a.Name.CompareTo(b.Name)); // Sort by name
                int profileIndex = Settings.Profiles.ProfileList.IndexOf(profile);
                _ProfileList.Insert(profileIndex, profileModel);
                ProfileSelected = profileModel;

                // TODO: [PICKER] Test and rework code if a ObservableCollection can be used for the picker instead!
                ProfileSelector?.ForceUpdate(); // Workaround as this should not be required!
            });
            ProfileDelete = new Command(() =>
            {
                if (App.CurrentApp.Settings.ReadOnlyMode) return;

                if (_ProfileList.Count <= 1) return; // do not delete the only last remaining profile

                TimerRunning = false;

                int profileIndex = _ProfileList.IndexOf(_ProfileSelected);

                // Change to next/previous profile before removing
                int Index = profileIndex + 1;
                if (_ProfileList.Count <= Index) Index = _ProfileList.Count - 2; // in case last profile is deleted, choose previous one
                ProfileSelected = _ProfileList[Index];

                // Removing
                Settings.Profiles.ProfileList.RemoveAt(profileIndex);
                _ProfileList.RemoveAt(profileIndex);

                // TODO: [PICKER] Test and rework code if a ObservableCollection can be used for the picker instead!
                ProfileSelector?.ForceUpdate(); // Workaround as this should not be required!
            });

            ProfileReset = new Command(() =>
            {
                TimerRunning = false;

                _ProfileSelected.Attempts++; // Increase attempts
                foreach (ProfileRowModel row in _ProfileSelected.Rows)
                {
                    if (!row.Active) // Check and update gold time when split is not finished
                    {
                        if ((0 < row.Duration) && (row.Duration < row.DurationGold)) row.DurationGold = row.Duration;
                    }
                    row.Hits = 0;
                    row.WayHits = 0;
                    row.Duration = 0;
                }
                _ProfileSelected.ActiveSplit = 0;
            });

            ProfilePB = new Command(() =>
            {
                TimerRunning = false;

                foreach (ProfileRowModel row in _ProfileSelected.Rows)
                {
                    row.PB = row.Hits + row.WayHits;
                    if ((0 < row.Duration) && (row.Duration < row.DurationGold)) row.DurationGold = row.Duration;
                    row.DurationPB = row.Duration;
                    row.SP = false;
                }
                _ProfileSelected.ActiveSplit = _ProfileSelected.Rows.Count - 1;
                _ProfileSelected.Rows[_ProfileSelected.Rows.Count - 1].SP = true;
                _ProfileSelected.SessionProgress = _ProfileSelected.Rows.Count - 1;
            });

            ProfileSetAttempts = new Command<int>((int NewAttempts) =>
            {
                _ProfileSelected.Attempts = NewAttempts;
                CallPropertyChanged(this, nameof(StatsProgress)); // TODO: Check if we need to update the 3 stats in any other command as well? (Done by OutputDataChangedHandler?)
            });

            ToggleTimerPause = new Command(() => TimerRunning = !TimerRunning);

            ToggleReadOnlyMode = new Command(() => IsReadOnly = !IsReadOnly);

            ProfileSplitMoveUp = new Command(() => {
                if (App.CurrentApp.Settings.ReadOnlyMode) return;

                UpdateDuration();
                _ProfileSelected.PermuteActiveSplit(-1);
            });
            ProfileSplitMoveDown = new Command(() => {
                if (App.CurrentApp.Settings.ReadOnlyMode) return;

                UpdateDuration();
                _ProfileSelected.PermuteActiveSplit(+1);
            });
            ProfileSplitInsert = new Command(() => {
                if (App.CurrentApp.Settings.ReadOnlyMode) return;

                UpdateDuration();
                _ProfileSelected.InsertNewRow();
            });

            HitIncrease = new Command(() => {
                UpdateDuration();
                _ProfileSelected.Rows[_ProfileSelected.ActiveSplit].Hits++;
            });
            HitDecrease = new Command(() => {
                UpdateDuration();
                _ProfileSelected.Rows[_ProfileSelected.ActiveSplit].Hits--;
            });
            HitWayIncrease = new Command(() => {
                UpdateDuration();
                _ProfileSelected.Rows[_ProfileSelected.ActiveSplit].WayHits++;
            });
            HitWayDecrease = new Command(() => {
                UpdateDuration();
                _ProfileSelected.Rows[_ProfileSelected.ActiveSplit].WayHits--;
            });
            SplitSelectNext = new Command(() =>
            {
                UpdateDuration();
                GoSplits(+1);
            });
            SplitSelectPrev = new Command(() =>
            {
                UpdateDuration();
                GoSplits(-1);
            });
        }

        public void OutputDataChangedHandler(object sender, PropertyChangedEventArgs e)
        {
            UpdateDuration();
            CallPropertyChanged(this, nameof(StatsProgress));
            CallPropertyChanged(this, nameof(StatsTime));
            CallPropertyChanged(this, nameof(StatsTotalHits));
            App.CurrentApp.om.Update(_ProfileSelected, TimerRunning);
        }
        private void CollectionChangedHandler(object sender, NotifyCollectionChangedEventArgs e) => OutputDataChangedHandler(sender, new PropertyChangedEventArgs(nameof(_ProfileList)));

        public ICommand CmdRemoveSplit { get; }
        private void RemoveSplit(ProfileRowModel item)
        {
            _ProfileSelected.DeleteRow(item);
        }

        public ICommand CmdSetActiveSplit { get; }
        private void SetActiveSplit(ProfileRowModel item)
        {
            if (item.Active) return;
            _ProfileSelected.ActiveSplit = _ProfileRowList.IndexOf(item);
        }

        public ICommand CmdSetSessionProgress { get; }
        private void SetSessionProgress(ProfileRowModel item)
        {
            if (item.SP) return;

            foreach(ProfileRowModel entry in _ProfileRowList) entry.SP = false;
            item.SP = true;

            _ProfileSelected.SessionProgress = _ProfileRowList.IndexOf(item);
        }

        // https://stackoverflow.com/questions/4500729/how-to-use-binding-in-the-listbox-s-items-to-the-viewmodel-s-properties

        private ObservableCollection<ProfileRowModel> _ProfileRowList => _ProfileSelected.Rows;

        // https://docs.microsoft.com/de-de/xamarin/xamarin-forms/user-interface/picker/populating-itemssource

        public static ObservableCollection<ProfileModel> _ProfileList { get; private set; }
        public ObservableCollection<ProfileModel> ProfileList { get => _ProfileList; } // TODO: Merge _ProfileList and ProfileList?

        public bool IsProfileExisting(string Name)
        {
            foreach (ProfileModel profileModel in _ProfileList)
            {
                if (profileModel.Name == Name) return true;
            }
            return false;
        }

        private ProfileModel _ProfileSelected = null;
        public ProfileModel ProfileSelected
        {
            get { return _ProfileSelected; }
            set
            {
                if (_ProfileSelected != value)
                {
                    if (_ProfileList.Contains(value))
                    {
                        UpdateDuration();

#if TODO // Stopping timer on RunCompleted - Still needed?
        public event EventHandler<EventArgs> ProfileChanged;
        public void ProfileChangedHandler(object sender, EventArgs e)
        {
            if (!Ready) return;
            
            if (e is ProfileChangedEventArgs)
            {
                ProfileChangedEventArgs eventArgs = (ProfileChangedEventArgs)e;
                if (eventArgs.RunCompleted && _TimerRunning)                                <--- TODO: Event to stop timer on RunCompleted!
                {
                    DateTime utc_now = DateTime.UtcNow;
                    timer1.Enabled = _TimerRunning = false;
                    SelectedProfileInfo.AddDuration((long)(utc_now - last_update_time).TotalMilliseconds);
                    last_update_time = utc_now;
                }
            }
        }
#endif
                        _ProfileSelected = value;

                        //SetAndNotifyWhenChanged(this, ref _ProfileSelected, value, nameof(ProfileSelected)); // TODO: Use this?
                        CallPropertyChanged(this, nameof(ProfileSelected));
                        OutputDataChangedHandler(this, new PropertyChangedEventArgs(nameof(ProfileSelected)));
                    }
                }
            }
        }

        public bool IsReadOnly
        {
            get => Settings.ReadOnlyMode;
            set
            {
                Settings.ReadOnlyMode = value;
                CallPropertyChanged(this, nameof(IsReadOnly));
            }
        }

        public ICommand ProfileNew { get; } 
        public ICommand ProfileRename { get; }
        public ICommand ProfileCopy { get; }
        public ICommand ProfileDelete { get; }

        public ICommand ProfileReset { get; }
        public ICommand ProfilePB { get; }
        public ICommand ProfileSetAttempts { get; }
        public ICommand ToggleTimerPause { get; }
        public ICommand ToggleReadOnlyMode { get; }

        public ICommand ProfileSplitMoveUp { get; }
        public ICommand ProfileSplitMoveDown { get; }
        public ICommand ProfileSplitInsert { get; }

        public ICommand HitIncrease { get; }
        public ICommand HitDecrease { get; }
        public ICommand HitWayIncrease { get; }
        public ICommand HitWayDecrease { get; }
        public ICommand SplitSelectNext { get; }
        public ICommand SplitSelectPrev { get; }

        private void GoSplits(int Amount)
        {
            int split = _ProfileSelected.ActiveSplit + Amount;
            if ((0 <= split) && (split < _ProfileRowList.Count))
            {
                // TODO: Allow no split to be active by out of range values?
                if ((0 < Amount) && _ProfileRowList[_ProfileSelected.ActiveSplit].SP) SetSessionProgress(_ProfileRowList[split]);
                _ProfileSelected.ActiveSplit = split;
            }
        }

        public string StatsProgress => "Progress:  " + _ProfileSelected.ActiveSplit.ToString() + " / " + _ProfileSelected.Rows.Count.ToString() + "  # " + _ProfileSelected.Attempts.ToString("D3");
        public string StatsTime
        {
            get
            {
                long TotalTime = 0;

                foreach (ProfileRowModel row in _ProfileSelected.Rows)
                {
                    TotalTime += row.Duration;
                }
                TotalTime /= 1000; // we only care about seconds

                return "Time: " + (TotalTime/60/60).ToString("D2") + " : " + ((TotalTime/60) % 60).ToString("D2") + " : " + (TotalTime % 60).ToString("D2");
            }
        }
        public string StatsTotalHits
        {
            get
            {
                int TotalHits = 0;
                int TotalPB = 0;

                foreach (ProfileRowModel row in _ProfileSelected.Rows)
                {
                    TotalHits += row.Hits + row.WayHits;
                    TotalPB += row.PB;
                }

                return "Total: " + TotalHits.ToString() + " Hits   " + TotalPB.ToString() + " PB";
            }
        }

#region Game Timer

        private DateTime last_update_time = DateTime.UtcNow;

        private bool _TimerRunning = false;
        public bool TimerRunning
        {
            get { return _TimerRunning; }
            set
            {
                if (value == _TimerRunning) return;
                if (_TimerRunning = value) // Set and prepare..
                {
                    // Starting the timer
                    App.StartApplicationTimer(TimerIDs.GameTime, 10, UpdateDuration);
                    App.StartApplicationTimer(TimerIDs.GameTimeGui, 150, () => { CallPropertyChanged(this, nameof(StatsTime)); return _TimerRunning; });
                }
                else
                {
                    // Stopping the timer
                    last_update_time = DateTime.UtcNow;
                    UpdateDuration();
                }
                CallPropertyChanged(this, nameof(TimerRunning));
                OutputDataChangedHandler(this, new PropertyChangedEventArgs(nameof(TimerRunning)));
            }
        }

        private readonly object TimerUpdateLock = new object();
        public bool UpdateDuration()
        {
            // Early cancellation point
            if (!_TimerRunning) return false;

            // sad there is no try-lock, so we use the "precise equivalent" of lock(){}
            // from: https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/lock-statement
            bool GotLock = false;
            try
            {
                System.Threading.Monitor.Enter(TimerUpdateLock, ref GotLock);

                DateTime utc_now = DateTime.UtcNow;
                
                _ProfileSelected.Rows[_ProfileSelected.ActiveSplit].Duration += (long)(utc_now - last_update_time).TotalMilliseconds;
                CallPropertyChanged(this, nameof(StatsTime));

                last_update_time = utc_now;
            }
            finally
            {
                if (GotLock) System.Threading.Monitor.Exit(TimerUpdateLock);
            }

            return _TimerRunning;
        }
#endregion
    }
}
