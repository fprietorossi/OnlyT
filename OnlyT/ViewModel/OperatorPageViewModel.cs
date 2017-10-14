﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight.Messaging;
using OnlyT.Models;
using OnlyT.Services.Options;
using OnlyT.Services.TalkSchedule;
using OnlyT.Services.Timer;
using OnlyT.Utils;
using OnlyT.ViewModel.Messages;
using Serilog;

namespace OnlyT.ViewModel
{
    /// <summary>
    /// View model for the Operator page
    /// </summary>
    public class OperatorPageViewModel : ViewModelBase, IPage
    {
        public static string PageName => "OperatorPage";
        private static readonly string _arrow = "→";
        private static readonly Brush _durationBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#f3dcbc"));
        private static readonly Brush _durationDimBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#bba991"));
        private readonly ITalkTimerService _timerService;
        private readonly ITalkScheduleService _scheduleService;
        private readonly IOptionsService _optionsService;
        private readonly IAdaptiveTimerService _adaptiveTimerService;
        private int _secondsElapsed;
        private static readonly Brush _whiteBrush = Brushes.White;
        private static readonly int _maxTimerMins = 99;
        private static readonly int _maxTimerSecs = _maxTimerMins * 60;

        private readonly SolidColorBrush _bellColorActive = new SolidColorBrush(Colors.Gold);
        private readonly SolidColorBrush _bellColorInactive = new SolidColorBrush(Colors.DarkGray);

        public OperatorPageViewModel(
           ITalkTimerService timerService,
           ITalkScheduleService scheduleService,
           IAdaptiveTimerService adaptiveTimerService,
           IOptionsService optionsService)
        {
            _scheduleService = scheduleService;
            _optionsService = optionsService;
            _adaptiveTimerService = adaptiveTimerService;
            _timerService = timerService;
            _timerService.TimerChangedEvent += TimerChangedHandler;

            SelectFirstTalk();

            StartCommand = new RelayCommand(StartTimer, () => IsNotRunning);
            StopCommand = new RelayCommand(StopTimer, () => IsRunning);
            SettingsCommand = new RelayCommand(NavigateSettings, () => IsNotRunning);
            HelpCommand = new RelayCommand(LaunchHelp);
            IncrementTimerCommand = new RelayCommand(IncrementTimer, CanIncreaseTimerValue);
            IncrementTimer15Command = new RelayCommand(IncrementTimer15Secs, CanIncreaseTimerValue);
            IncrementTimer5Command = new RelayCommand(IncrementTimer5Mins, CanIncreaseTimerValue);
            DecrementTimerCommand = new RelayCommand(DecrementTimer, CanDecreaseTimerValue);
            DecrementTimer15Command = new RelayCommand(DecrementTimer15Secs, CanDecreaseTimerValue);
            DecrementTimer5Command = new RelayCommand(DecrementTimer5Mins, CanDecreaseTimerValue);
            BellToggleCommand = new RelayCommand(BellToggle);

            // subscriptions...
            Messenger.Default.Register<OperatingModeChangedMessage>(this, OnOperatingModeChanged);
            Messenger.Default.Register<AutoMeetingChangedMessage>(this, OnAutoMeetingChanged);
        }

        private void LaunchHelp()
        {
            System.Diagnostics.Process.Start(@"https://github.com/AntonyCorbett/OnlyT/wiki");
        }

        private void BellToggle()
        {
            var talk = GetCurrentTalk();
            if (talk != null)
            {
                talk.Bell = !talk.Bell;
                RaisePropertyChanged(nameof(BellColour));
                RaisePropertyChanged(nameof(BellTooltip));
            }
        }

        public SolidColorBrush BellColour
        {
            get
            {
                var talk = GetCurrentTalk();
                if (talk != null)
                {
                    return talk.Bell
                       ? _bellColorActive
                       : _bellColorInactive;
                }
                return _bellColorInactive;
            }
        }

        public string BellTooltip
        {
            get
            {
                var talk = GetCurrentTalk();
                if (talk != null)
                {
                    return talk.Bell
                       ? Properties.Resources.ACTIVE_BELL
                       : Properties.Resources.INACTIVE_BELL;
                }

                return string.Empty;
            }
        }

        private bool CanIncreaseTimerValue()
        {
            if (IsRunning || TargetSeconds >= _maxTimerSecs)
            {
                return false;
            }

            return CurrentTalkTimerIsEditable();
        }

        private bool CanDecreaseTimerValue()
        {
            if (IsRunning || TargetSeconds <= 0)
            {
                return false;
            }

            return CurrentTalkTimerIsEditable();
        }

        private bool CurrentTalkTimerIsEditable()
        {
            var talk = GetCurrentTalk();
            return talk == null || talk.Editable;
        }

        private void OnAutoMeetingChanged(AutoMeetingChangedMessage message)
        {
            try
            {
                _scheduleService.Reset();
                TalkId = 0;
                RaisePropertyChanged(nameof(Talks));
                SelectFirstTalk();
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Could not handle change of meeting schedule");
            }
        }

        private void IncrDecrTimerInternal(int mins)
        {
            var newSecs = TargetSeconds + mins;
            if (newSecs >= 0 && newSecs <= _maxTimerSecs)
            {
                TargetSeconds = newSecs;
                AdjustTalkTimeForThisSession();
            }
        }

        private void DecrementTimer()
        {
            IncrDecrTimerInternal(-60);
        }

        private void DecrementTimer15Secs()
        {
            IncrDecrTimerInternal(-15);
        }

        private void DecrementTimer5Mins()
        {
            IncrDecrTimerInternal(-5 * 60);
        }

        private void IncrementTimer()
        {
            IncrDecrTimerInternal(60);
        }

        private void IncrementTimer15Secs()
        {
            IncrDecrTimerInternal(15);
        }
        private void IncrementTimer5Mins()
        {
            IncrDecrTimerInternal(5 * 60);
        }

        private void OnOperatingModeChanged(OperatingModeChangedMessage message)
        {
            RaisePropertyChanged(nameof(IsManualMode));
            RaisePropertyChanged(nameof(IsNotManualMode));
            Messenger.Default.Send(new AutoMeetingChangedMessage());
        }

        private void SelectFirstTalk()
        {
            var talks = _scheduleService.GetTalkScheduleItems()?.ToArray();
            if (talks != null && talks.Any())
            {
                TalkId = talks.First().Id;
            }
        }

        private void NavigateSettings()
        {
            Messenger.Default.Send(new NavigateMessage(PageName, SettingsPageViewModel.PageName, null));
        }

        private void StopTimer()
        {
            _timerService.Stop();
            _isStarting = false;
            
            TextColor = _whiteBrush;

            RaisePropertyChanged(nameof(IsRunning));
            RaisePropertyChanged(nameof(IsNotRunning));

            Messenger.Default.Send(new TimerStopMessage());
            RaiseCanExecuteChanged();

            AutoAdvance();
        }

        private void AutoAdvance()
        {
            // advance to next item...
            TalkId = _scheduleService.GetNext(TalkId);
        }

        public bool IsManualMode => _optionsService.Options.OperatingMode == OperatingMode.Manual;
        public bool IsNotManualMode => _optionsService.Options.OperatingMode != OperatingMode.Manual;


        public bool IsRunning => _timerService.IsRunning || _isStarting;
        public bool IsNotRunning => !IsRunning;

        private bool _isStarting;

        private void StartTimer()
        {
            _isStarting = true;
            _secondsElapsed = 0;

            RunFlashAnimation = false;
            RunFlashAnimation = true;

            RaisePropertyChanged(nameof(IsRunning));
            RaisePropertyChanged(nameof(IsNotRunning));

            RaiseCanExecuteChanged();
            AdjustForAdaptiveTime();

            Messenger.Default.Send(new TimerStartMessage(_targetSeconds));

            Task.Run(() =>
            {
                int ms = DateTime.Now.Millisecond;
                if (ms > 100)
                {
                    // sync to the second (so that the timer window clock and countdown
                    // seconds are in sync)...

                    Task.Delay(1000 - ms).Wait();
                }

                if (_isStarting)
                {
                    _timerService.Start(_targetSeconds);
                }
            });
        }

        private void AdjustForAdaptiveTime()
        {
            try
            {
                if (TalkId > 0)
                {
                    var newDuration = _adaptiveTimerService.CalculateAdaptedDuration(TalkId);
                    if (newDuration != null)
                    {
                        var talk = GetCurrentTalk();
                        if(talk != null)
                        { 
                            talk.AdaptedDuration = newDuration.Value;
                            SetDurationStringAttributes(talk);
                            TargetSeconds = (int)talk.ActualDuration.TotalSeconds;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Could not adjust for adaptive time");
            }
        }

        private void RaiseCanExecuteChanged()
        {
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            SettingsCommand.RaiseCanExecuteChanged();

            RaiseCanExecuteIncrDecrChanged();
        }

        private void RaiseCanExecuteIncrDecrChanged()
        {
            IncrementTimerCommand?.RaiseCanExecuteChanged();
            IncrementTimer5Command?.RaiseCanExecuteChanged();
            IncrementTimer15Command?.RaiseCanExecuteChanged();

            DecrementTimerCommand?.RaiseCanExecuteChanged();
            DecrementTimer5Command?.RaiseCanExecuteChanged();
            DecrementTimer15Command?.RaiseCanExecuteChanged();
        }

        public IEnumerable<TalkScheduleItem> Talks => _scheduleService.GetTalkScheduleItems();

        private int _talkId;
        public int TalkId
        {
            get => _talkId;
            set
            {
                if (_talkId != value)
                {
                    _talkId = value;
                    TargetSeconds = GetTargetSecondsFromTalkSchedule(_talkId);
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(IsBellVisible));
                    RaisePropertyChanged(nameof(BellColour));
                    RaisePropertyChanged(nameof(BellTooltip));
                    
                    RaiseCanExecuteIncrDecrChanged();

                    var talk = _scheduleService.GetTalkScheduleItem(_talkId);
                    SetDurationStringAttributes(talk);
                }
            }
        }

        private TalkScheduleItem GetCurrentTalk()
        {
            return _scheduleService.GetTalkScheduleItem(TalkId);
        }

        private int GetTargetSecondsFromTalkSchedule(int talkId)
        {
            var talk = _scheduleService.GetTalkScheduleItem(talkId);
            return talk?.GetDurationSeconds() ?? 0;
        }

        private bool _runFlashAnimation;
        public bool RunFlashAnimation
        {
            get => _runFlashAnimation;
            set
            {
                if (_runFlashAnimation != value)
                {
                    TextColor = new SolidColorBrush(Colors.White);
                    _runFlashAnimation = value;
                    RaisePropertyChanged();
                }
            }
        }

        private Brush _textColor = Brushes.White;
        public Brush TextColor
        {
            get => _textColor;
            set
            {
                _textColor = value;
                RaisePropertyChanged();
            }
        }

        private void TimerChangedHandler(object sender, EventArgs.TimerChangedEventArgs e)
        {
            TextColor = GreenYellowRedSelector.GetBrushForTimeRemaining(e.RemainingSecs);
            _secondsElapsed = e.ElapsedSecs;
            SecondsRemaining = e.RemainingSecs;
            
            Messenger.Default.Send(new TimerChangedMessage(e.RemainingSecs, e.ElapsedSecs, e.IsRunning));

            if (e.RemainingSecs == 0)
            {
                // broadcast talk overtime...
                var talk = GetCurrentTalk();
                if (talk != null)
                {
                    Messenger.Default.Send(new OvertimeMessage(talk.Name, talk.Bell, e.TargetSecs));
                }
            }
        }

        private int _targetSeconds;
        
        private int TargetSeconds
        {
            get => _targetSeconds;
            set
            {
                if (_targetSeconds != value)
                {
                    _targetSeconds = value;
                    SecondsRemaining = _targetSeconds;
                    
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(CurrentTimerValueString));
                    RaiseCanExecuteIncrDecrChanged();
                }
            }
        }

        private void SetDurationStringAttributes(TalkScheduleItem talk)
        {
            if (talk != null)
            {
                Duration1String = TimeFormatter.FormatTimerDisplayString((int)talk.OriginalDuration.TotalSeconds);
                Duration1Tooltip = Properties.Resources.DURATION_ORIGINAL;

                if (talk.ModifiedDuration != null)
                {
                    Duration2String = TimeFormatter.FormatTimerDisplayString((int) talk.ModifiedDuration.Value.TotalSeconds);
                    Duration2Tooltip = Properties.Resources.DURATION_MODIFIED;

                    if (talk.AdaptedDuration != null)
                    {
                        Duration3String = TimeFormatter.FormatTimerDisplayString((int) talk.AdaptedDuration.Value.TotalSeconds);
                        Duration3Tooltip = Properties.Resources.DURATION_ADAPTED;
                    }
                    else
                    {
                        Duration3String = string.Empty;
                    }
                }
                else if (talk.AdaptedDuration != null)
                {
                    Duration2String = TimeFormatter.FormatTimerDisplayString((int)talk.AdaptedDuration.Value.TotalSeconds);
                    Duration2Tooltip = Properties.Resources.DURATION_ADAPTED;
                    Duration3String = string.Empty;
                }
                else
                {
                    Duration2String = string.Empty;
                    Duration3String = string.Empty;
                }
            }
            else
            {
                Duration1String = string.Empty;
                Duration2String = string.Empty;
                Duration3String = string.Empty;
            }

            Duration1Colour = _durationDimBrush;
            Duration2Colour = _durationDimBrush;
            Duration3Colour = _durationDimBrush;
            
            if (!string.IsNullOrEmpty(Duration3String))
            {
                Duration3Colour = _durationBrush;
            }
            else if (!string.IsNullOrEmpty(Duration2String))
            {
                Duration2Colour = _durationBrush;
            }
            else
            {
                Duration1Colour = _durationBrush;
            }
            
            RaisePropertyChanged(nameof(Duration1Colour));
            RaisePropertyChanged(nameof(Duration2Colour));
            RaisePropertyChanged(nameof(Duration3Colour));

            RaisePropertyChanged(nameof(Duration1Tooltip));
            RaisePropertyChanged(nameof(Duration2Tooltip));
            RaisePropertyChanged(nameof(Duration3Tooltip));
        }

        public string Duration1ArrowString { get; set; }
        public string Duration2ArrowString { get; set; }
        public Brush Duration1Colour { get; set; }
        public Brush Duration2Colour { get; set; }
        public Brush Duration3Colour { get; set; }
        public string Duration1Tooltip { get; set; }
        public string Duration2Tooltip { get; set; }
        public string Duration3Tooltip { get; set; }


        private string _duration1String;
        public string Duration1String
        {
            get => _duration1String;
            set
            {
                _duration1String = value;
                RaisePropertyChanged();
            }
        }

        private string _duration2String;
        public string Duration2String
        {
            get => _duration2String;
            set
            {
                _duration2String = value;
                
                RaisePropertyChanged();
                Duration1ArrowString = string.IsNullOrEmpty(_duration2String) ? string.Empty : _arrow;
                RaisePropertyChanged(nameof(Duration1ArrowString));
            }
        }

        private string _duration3String;
        public string Duration3String
        {
            get => _duration3String;
            set
            {
                _duration3String = value;
                RaisePropertyChanged();
                Duration2ArrowString = string.IsNullOrEmpty(_duration3String) ? string.Empty : _arrow;
                RaisePropertyChanged(nameof(Duration2ArrowString));
            }
        }

        private void AdjustTalkTimeForThisSession()
        {
            var talk = GetCurrentTalk();
            if (talk != null && talk.Editable)
            {
                talk.ModifiedDuration = TimeSpan.FromSeconds(TargetSeconds);
                SetDurationStringAttributes(talk);
            }
        }

        private int _secondsRemaining;

        private int SecondsRemaining
        {
            get => _secondsRemaining;
            set
            {
                if (_secondsRemaining != value)
                {
                    _secondsRemaining = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(CurrentTimerValueString));
                }
            }
        }
        
        public string CurrentTimerValueString => TimeFormatter.FormatTimerDisplayString(_optionsService.Options.CountUp
            ? _secondsElapsed
            : _secondsRemaining);

        public bool IsBellVisible
        {
            get
            {
                var talk = GetCurrentTalk();
                return talk != null && talk.OriginalBell && _optionsService.Options.IsBellEnabled;
            }
        }

        public bool IsCircuitVisit => _optionsService.Options.IsCircuitVisit &&
                                      _optionsService.Options.OperatingMode == OperatingMode.Automatic;

        public void Activated(object state)
        {
            RaisePropertyChanged(nameof(CurrentTimerValueString));  // "CountUp" setting may have changed
            RaisePropertyChanged(nameof(IsBellVisible));
            RaisePropertyChanged(nameof(IsCircuitVisit));
        }

        public RelayCommand StartCommand { get; set; }
        public RelayCommand StopCommand { get; set; }
        public RelayCommand SettingsCommand { get; set; }
        public RelayCommand HelpCommand { get; set; }
        public RelayCommand IncrementTimerCommand { get; set; }
        public RelayCommand IncrementTimer15Command { get; set; }
        public RelayCommand IncrementTimer5Command { get; set; }
        public RelayCommand DecrementTimerCommand { get; set; }
        public RelayCommand DecrementTimer15Command { get; set; }
        public RelayCommand DecrementTimer5Command { get; set; }
        public RelayCommand BellToggleCommand { get; set; }
    }
}
