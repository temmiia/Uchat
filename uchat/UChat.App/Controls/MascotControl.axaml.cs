using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace UChat.App.Controls;

/// <summary>
/// Interactive mascot with eye tracking, jump, walk animations, and periodic title "theft".
/// </summary>
public partial class MascotControl : UserControl
{
    // ---- Mascot animation settings ----
    private const double WalkSpeed = 80;               // walk speed, px/s
    private const double StepsPerSecond = 2;           // full step cycles (both legs) per second
    private const double HoldTime = 3;                 // how long the mascot holds the stolen text
    private const double CarriedTextScale = 0.6;
    private const double MinStealIntervalSeconds = 25; // minimum delay before stealing title
    private const double MaxStealIntervalSeconds = 45; // maximum delay before stealing title

    // Visible mascot bounds inside 100x100 container (matching mascot.png dimensions)
    private const double MascotBodyLeft = 9;
    private const double MascotBodyCenter = 46;
    private const double MascotHeadTop = 18;

    private enum MascotState
    {
        Idle,           // idle at home
        Telekinesis,    // lifting right leg and levitating a UI element
        WalkToTitle,    // walking to title
        Grab,           // grabbing title
        CarryHome,      // carrying title home
        Hold,           // holding title on head
        CarryBack,      // carrying title back
        Release,        // returning title to place
        WalkHome        // walking home
    }

    public static readonly StyledProperty<Control?> TargetTitleProperty =
        AvaloniaProperty.Register<MascotControl, Control?>(nameof(TargetTitle));

    public static readonly StyledProperty<string> MascotColorProperty =
        AvaloniaProperty.Register<MascotControl, string>(nameof(MascotColor), defaultValue: "Blue");

    public static readonly StyledProperty<string> MascotHatProperty =
        AvaloniaProperty.Register<MascotControl, string>(nameof(MascotHat), defaultValue: "None");

    public string MascotColor
    {
        get => GetValue(MascotColorProperty);
        set => SetValue(MascotColorProperty, value);
    }

    public string MascotHat
    {
        get => GetValue(MascotHatProperty);
        set => SetValue(MascotHatProperty, value);
    }

    private readonly TranslateTransform _leftPupilOffset = new();
    private readonly TranslateTransform _rightPupilOffset = new();
    private readonly ScaleTransform _mascotScale = new(1, 1);
    private readonly TranslateTransform _mascotPosition = new();
    private readonly RotateTransform _mascotRotation = new();
    private readonly TranslateTransform _leftLegPosition = new();
    private readonly TranslateTransform _rightLegPosition = new();
    private readonly ScaleTransform _rightLegMirror = new()
    {
        ScaleX = -1
    };

    // Transforms for title stolen by mascot
    private readonly ScaleTransform _titleScale = new(1, 1);
    private readonly RotateTransform _titleRotation = new();
    private readonly TranslateTransform _titleOffset = new();
    private readonly TransformGroup _titleTransformGroup;

    private readonly Stopwatch _mascotClock = Stopwatch.StartNew();
    private readonly DispatcherTimer _mascotAnimation = new()
    {
        Interval = TimeSpan.FromMilliseconds(16)
    };

    private TopLevel? _attachedTopLevel;
    private double _jumpStart = -1;

    // Stealing state
    private MascotState _state = MascotState.Idle;
    private double _stateStart;
    private double _stateFrom;
    private double _stateTo;
    private double _stateDuration = double.PositiveInfinity;
    private double _lastTick;
    private double _textCarry;   // 0 = title in place, 1 = title on mascot's head

    // Walk cycle
    private double _walkWeight;  // 0..1, smooth walk weight transition
    private double _walkPhase;
    private double _walkDir = -1;
    private double _walkBob;
    private double _walkTilt;
    private double _leftStepY;
    private double _rightStepY;

    // Telekinesis state
    private Control? _teleTarget;
    private ITransform? _originalTargetTransform;
    private readonly TranslateTransform _teleOffset = new();
    private readonly RotateTransform _teleRotate = new();
    private TransformGroup? _teleGroup;
    private double _teleLegLift;
    private double _teleMascotTilt;
    private bool _lastWasTelekinesis;

    public Control? TargetTitle
    {
        get => GetValue(TargetTitleProperty);
        set => SetValue(TargetTitleProperty, value);
    }

    public MascotControl()
    {
        InitializeComponent();

        LeftPupil.RenderTransform = _leftPupilOffset;
        RightPupil.RenderTransform = _rightPupilOffset;

        // Order matters: scale and tilt first, then translate,
        // otherwise the mascot would swing around the old anchor when walking.
        MascotContainer.RenderTransform = new TransformGroup
        {
            Children = { _mascotScale, _mascotRotation, _mascotPosition }
        };
        LeftLeg.RenderTransform = _leftLegPosition;
        RightLeg.RenderTransform = new TransformGroup
        {
            Children = { _rightLegMirror, _rightLegPosition }
        };
        _titleTransformGroup = new TransformGroup
        {
            Children = { _titleScale, _titleRotation, _titleOffset }
        };

        _mascotAnimation.Tick += OnMascotAnimationTick;

        UpdateMascotColor(MascotColor);
        UpdateMascotHat(MascotHat);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TargetTitleProperty)
        {
            if (change.OldValue is Control oldTarget && ReferenceEquals(oldTarget.RenderTransform, _titleTransformGroup))
            {
                oldTarget.RenderTransform = null;
            }

            if (change.NewValue is Control newTarget)
            {
                newTarget.RenderTransform = _titleTransformGroup;
            }
        }
        else if (change.Property == MascotColorProperty)
        {
            UpdateMascotColor(change.GetNewValue<string>());
        }
        else if (change.Property == MascotHatProperty)
        {
            UpdateMascotHat(change.GetNewValue<string>());
        }
    }

    private static Bitmap LoadBitmap(string assetName)
    {
        var uri = new Uri($"avares://UChat.App/Assets/{assetName}");
        return new Bitmap(AssetLoader.Open(uri));
    }

    private void UpdateMascotColor(string? color)
    {
        if (MascotBody is null || LeftLeg is null || RightLeg is null)
            return;

        var colorKey = color?.ToLowerInvariant() ?? "blue";
        string bodyFile = colorKey switch
        {
            "purple" => "mascot_purple.png",
            "green" => "mascot_green.png",
            "blue" or "lightblue" => "mascot_lightblue.png",
            _ => "mascot_pink.png"
        };
        string legFile = colorKey switch
        {
            "purple" => "leg_purple.png",
            "green" => "leg_green.png",
            "blue" or "lightblue" => "leg_lightblue.png",
            _ => "leg_pink.png"
        };

        try
        {
            MascotBody.Source = LoadBitmap(bodyFile);
            var legBmp = LoadBitmap(legFile);
            LeftLeg.Source = legBmp;
            RightLeg.Source = legBmp;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Mascot] Error updating color: {ex.Message}");
        }
    }

    private void UpdateMascotHat(string? hat)
    {
        var hatKey = hat?.ToLowerInvariant() ?? "none";

        if (WitchHat is not null)
            WitchHat.IsVisible = hatKey == "witch";

        if (LuffyHat is not null)
            LuffyHat.IsVisible = hatKey == "luffy";
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _attachedTopLevel = TopLevel.GetTopLevel(this);
        _attachedTopLevel?.AddHandler(PointerMovedEvent, OnHostPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        var now = _mascotClock.Elapsed.TotalSeconds;
        _lastTick = now;
        if (_state == MascotState.Idle && double.IsPositiveInfinity(_stateDuration))
        {
            BeginWait(MascotState.Idle, now, GetNextStealDelay());
        }

        _mascotAnimation.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        EndTelekinesis();
        _mascotAnimation.Stop();
        _attachedTopLevel?.RemoveHandler(PointerMovedEvent, OnHostPointerMoved);
        _attachedTopLevel = null;

        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_state == MascotState.Telekinesis && _teleTarget is not null && _teleTarget.IsLoaded)
        {
            var targetBounds = _teleTarget.Bounds;
            var targetCenter = _teleTarget.TranslatePoint(new Point(targetBounds.Width / 2, targetBounds.Height / 2), MascotContainer);
            if (targetCenter.HasValue)
            {
                LookAt(targetCenter.Value.X, targetCenter.Value.Y);
                return;
            }
        }

        var cursor = e.GetPosition(MascotContainer);
        LookAt(cursor.X, cursor.Y);
    }

    private void LookAt(double x, double y)
    {
        _leftPupilOffset.X = Math.Clamp((x - 33.1) / 75 * 2.8, -2.8, 2.8);
        _leftPupilOffset.Y = Math.Clamp((y - 41.3) / 55 * 2.2, -2.2, 2.2);
        _rightPupilOffset.X = Math.Clamp((x - 60.7) / 75 * 2.8, -2.8, 2.8);
        _rightPupilOffset.Y = Math.Clamp((y - 41.2) / 55 * 2.2, -2.2, 2.2);
    }

    private void OnMascotPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(MascotContainer).Properties;

        // Double-click or Middle-click: trigger Telekinesis
        if ((e.ClickCount >= 2 || properties.IsMiddleButtonPressed) && _state == MascotState.Idle)
        {
            BeginTelekinesis(_mascotClock.Elapsed.TotalSeconds);
            e.Handled = true;
            return;
        }

        // Right-click: immediately go steal "Messages" title!
        if (properties.IsRightButtonPressed && _state == MascotState.Idle)
        {
            if (TargetTitle is { Bounds.Width: > 0 })
            {
                BeginMove(MascotState.WalkToTitle, GetTitleStopX(), _mascotClock.Elapsed.TotalSeconds);
            }
            else
            {
                BeginTelekinesis(_mascotClock.Elapsed.TotalSeconds);
            }
            e.Handled = true;
            return;
        }

        // Left-click: jump
        if (properties.IsLeftButtonPressed)
        {
            _jumpStart = _mascotClock.Elapsed.TotalSeconds;
        }
    }

    private void OnMascotAnimationTick(object? sender, EventArgs e)
    {
        var elapsed = _mascotClock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(elapsed - _lastTick, 0, 0.05);
        _lastTick = elapsed;

        UpdateMascotState(elapsed);
        UpdateWalkCycle(dt);

        var jump = 0d;
        var jumpProgress = 0d;

        if (_jumpStart >= 0)
        {
            var jumpElapsed = elapsed - _jumpStart;
            if (jumpElapsed < 0.9)
            {
                jump = Math.Sin(jumpElapsed / 0.9 * Math.PI);
                jumpProgress = jumpElapsed / 0.9;
            }
            else if (jumpElapsed >= 1.02)
            {
                _jumpStart = -1;
            }
        }

        var wobble = jump > 0
            ? Math.Sin(jumpProgress * Math.PI * 4) * 8 * jump
            : 0d;

        _mascotPosition.Y = -20 * jump + _walkBob;
        _mascotScale.ScaleX = 1 + 0.075 * jump;
        _mascotScale.ScaleY = 1 - 0.065 * jump;
        _mascotRotation.Angle = wobble + _walkTilt + _teleMascotTilt;

        var leftLegJump = GetDelayedJump(elapsed, 0.06);
        var rightLegJump = GetDelayedJump(elapsed, 0.3);
        _leftLegPosition.Y = -5 * leftLegJump + _leftStepY;
        _rightLegPosition.Y = -5 * rightLegJump + _rightStepY + _teleLegLift;

        if (_state == MascotState.Telekinesis)
        {
            var teleIntensity = Math.Sin(Math.Clamp((elapsed - _stateStart) / _stateDuration, 0, 1) * Math.PI);
            var pulse = Math.Sin(elapsed * 20) * 0.8 * teleIntensity;
            _leftPupilOffset.X += pulse;
            _rightPupilOffset.X += pulse;
        }

        UpdateTitle();
    }

    private double GetDelayedJump(double elapsed, double delay)
    {
        if (_jumpStart < 0)
        {
            return 0;
        }

        var legElapsed = elapsed - _jumpStart - delay;
        if (legElapsed < 0 || legElapsed >= 0.9)
        {
            return 0;
        }

        return Math.Sin(legElapsed / 0.9 * Math.PI);
    }

    // ------------------------------------------------------------------
    // Sequence: wait 25–45s -> walk to title -> grab -> carry home
    // -> hold on head -> carry back -> put back -> walk home
    // ------------------------------------------------------------------
    private void UpdateMascotState(double now)
    {
        var t = now - _stateStart;

        switch (_state)
        {
            case MascotState.Idle:
                if (t >= _stateDuration)
                {
                    if (!_lastWasTelekinesis)
                    {
                        _lastWasTelekinesis = true;
                        BeginTelekinesis(now);
                    }
                    else if (TargetTitle is { Bounds.Width: > 0 })
                    {
                        _lastWasTelekinesis = false;
                        BeginMove(MascotState.WalkToTitle, GetTitleStopX(), now);
                    }
                    else
                    {
                        BeginTelekinesis(now);
                    }
                }
                break;

            case MascotState.Telekinesis:
                var teleP = Math.Clamp(t / _stateDuration, 0, 1);
                UpdateTelekinesis(teleP, now);
                if (t >= _stateDuration)
                {
                    EndTelekinesis();
                    BeginWait(MascotState.Idle, now, GetNextStealDelay());
                }
                break;

            case MascotState.WalkToTitle:
                if (StepMove(t))
                {
                    BeginWait(MascotState.Grab, now, 0.6);
                    _jumpStart = now; // jumps when grabbing title
                }
                break;

            case MascotState.Grab:
                _textCarry = Smooth(t / _stateDuration);
                if (t >= _stateDuration)
                {
                    _textCarry = 1;
                    BeginMove(MascotState.CarryHome, 0, now);
                }
                break;

            case MascotState.CarryHome:
                if (StepMove(t))
                {
                    BeginWait(MascotState.Hold, now, HoldTime);
                }
                break;

            case MascotState.Hold:
                if (t >= _stateDuration)
                {
                    BeginMove(MascotState.CarryBack, GetTitleStopX(), now);
                }
                break;

            case MascotState.CarryBack:
                if (StepMove(t))
                {
                    BeginWait(MascotState.Release, now, 0.6);
                }
                break;

            case MascotState.Release:
                _textCarry = 1 - Smooth(t / _stateDuration);
                if (t >= _stateDuration)
                {
                    _textCarry = 0;
                    BeginMove(MascotState.WalkHome, 0, now);
                }
                break;

            case MascotState.WalkHome:
                if (StepMove(t))
                {
                    BeginWait(MascotState.Idle, now, GetNextStealDelay());
                }
                break;
        }
    }

    private static double GetNextStealDelay() =>
        MinStealIntervalSeconds + Random.Shared.NextDouble() * (MaxStealIntervalSeconds - MinStealIntervalSeconds);

    private void BeginMove(MascotState next, double toX, double now)
    {
        _state = next;
        _stateStart = now;
        _stateFrom = _mascotPosition.X;
        _stateTo = toX;
        _stateDuration = Math.Max(0.01, Math.Abs(toX - _stateFrom) / WalkSpeed);

        if (Math.Abs(toX - _stateFrom) > 0.01)
        {
            _walkDir = Math.Sign(toX - _stateFrom);
        }
    }

    private void BeginWait(MascotState next, double now, double duration)
    {
        _state = next;
        _stateStart = now;
        _stateDuration = duration;
    }

    private void BeginTelekinesis(double now)
    {
        _teleTarget = FindTelekinesisTarget();
        if (_teleTarget is null)
        {
            BeginWait(MascotState.Idle, now, GetNextStealDelay());
            return;
        }

        _originalTargetTransform = _teleTarget.RenderTransform;
        _teleGroup = new TransformGroup
        {
            Children = { _teleOffset, _teleRotate }
        };
        if (_originalTargetTransform is Transform t)
        {
            _teleGroup.Children.Insert(0, t);
        }

        _teleTarget.RenderTransform = _teleGroup;
        BeginWait(MascotState.Telekinesis, now, 3.2);
    }

    private void UpdateTelekinesis(double p, double now)
    {
        var intensity = Math.Sin(p * Math.PI);
        _teleLegLift = -15 * Math.Pow(intensity, 0.7);
        _teleMascotTilt = Math.Sin(now * 12) * 1.5 * intensity - 3.5 * intensity;

        if (_teleTarget is not null && _teleTarget.IsLoaded)
        {
            _teleOffset.Y = -22 * Math.Pow(intensity, 0.75) + Math.Sin(now * 6) * 3.5 * intensity;
            _teleOffset.X = Math.Sin(now * 4.5) * 2 * intensity;
            _teleRotate.Angle = Math.Sin(now * 5) * 3 * intensity;
        }
    }

    private void EndTelekinesis()
    {
        if (_teleTarget is not null)
        {
            _teleTarget.RenderTransform = _originalTargetTransform;
            _teleTarget = null;
            _originalTargetTransform = null;
            _teleGroup = null;
        }
        _teleLegLift = 0;
        _teleMascotTilt = 0;
    }

    private Control? FindTelekinesisTarget()
    {
        if (_attachedTopLevel is null)
            return null;

        var messages = new List<Control>();
        var otherControls = new List<Control>();

        foreach (var visual in _attachedTopLevel.GetVisualDescendants())
        {
            if (visual is not Control control || !control.IsVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
                continue;

            if (control == this || control == MascotContainer || control == TargetTitle)
                continue;

            if (control is Border border && border.Classes.Contains("Msg"))
            {
                messages.Add(border);
            }
            else if (control is Button btn && (btn.Classes.Contains("ChatAction") || btn.Classes.Contains("Primary") || btn.Classes.Contains("LoadOlderBtn") || btn.Classes.Contains("CaptionBtn")))
            {
                otherControls.Add(btn);
            }
            else if (control is Border bCard && bCard.Classes.Contains("ChatCard"))
            {
                otherControls.Add(bCard);
            }
        }

        if (messages.Count > 0 && (otherControls.Count == 0 || Random.Shared.NextDouble() < 0.6))
        {
            return messages[Random.Shared.Next(messages.Count)];
        }

        if (otherControls.Count > 0)
        {
            return otherControls[Random.Shared.Next(otherControls.Count)];
        }

        return messages.Count > 0 ? messages[Random.Shared.Next(messages.Count)] : null;
    }

    // Returns true when mascot reaches destination.
    private bool StepMove(double t)
    {
        var p = Math.Clamp(t / _stateDuration, 0, 1);
        _mascotPosition.X = _stateFrom + (_stateTo - _stateFrom) * p;
        return p >= 1;
    }

    private bool IsWalking =>
        _state is MascotState.WalkToTitle
            or MascotState.CarryHome
            or MascotState.CarryBack
            or MascotState.WalkHome;

    // Mascot offset (relative to home) positioning it next to right edge of title.
    private double GetTitleStopX()
    {
        if (TargetTitle is not { Bounds: { Width: > 0 } title })
        {
            return 0;
        }

        return title.Right + 6 - (Bounds.X + MascotBodyLeft);
    }

    // ------------------------------------------------------------------
    // Walk animation: legs alternate, body bobs and
    // waddles side to side. _walkWeight smoothly blends it in and out.
    // ------------------------------------------------------------------
    private void UpdateWalkCycle(double dt)
    {
        var target = IsWalking ? 1d : 0d;
        _walkWeight += (target - _walkWeight) * Math.Min(1, dt * 10);
        _walkPhase += dt * StepsPerSecond * Math.PI * 2 * _walkWeight;

        var sin = Math.Sin(_walkPhase);
        var cos = Math.Cos(_walkPhase);
        var w = _walkWeight;

        // Each leg lifts only during its half of the cycle
        _leftStepY = -5 * Math.Max(0, sin) * w;
        _rightStepY = -5 * Math.Max(0, -sin) * w;

        // Leg in the air moves forward along walking direction
        _leftLegPosition.X = -cos * 3 * _walkDir * w;
        _rightLegPosition.X = cos * 3 * _walkDir * w;

        _walkBob = -2 * Math.Abs(sin) * w;
        _walkTilt = (3 * sin + 2 * _walkDir) * w;
    }

    // ------------------------------------------------------------------
    // "Messages" title: in place (carry = 0) or sitting on mascot's
    // head scaled down (carry = 1). Moves in an arc between them.
    // ------------------------------------------------------------------
    private void UpdateTitle()
    {
        if (TargetTitle is not { Bounds: { Width: > 0 } title })
        {
            return;
        }

        var mascot = Bounds;
        var p = _textCarry;
        var centerX = title.X + title.Width / 2;
        var centerY = title.Y + title.Height / 2;

        var targetX = mascot.X + _mascotPosition.X + MascotBodyCenter;
        var targetY = mascot.Y + MascotHeadTop - 5 + _mascotPosition.Y;

        var arc = -18 * Math.Sin(Math.PI * p); // upward arc during flight

        var scale = 1 - (1 - CarriedTextScale) * p;
        _titleScale.ScaleX = scale;
        _titleScale.ScaleY = scale;
        _titleOffset.X = (targetX - centerX) * p;
        _titleOffset.Y = (targetY - centerY) * p + arc;
        _titleRotation.Angle = _mascotRotation.Angle * p;
    }

    private static double Smooth(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }
}
