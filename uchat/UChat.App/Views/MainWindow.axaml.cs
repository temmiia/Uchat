using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
using UChat.App.ViewModels;

namespace UChat.App.Views;

public partial class MainWindow : Window
{
    // ---- Налаштування анімації мускота (можна міняти під себе) ----
    private const double WalkSpeed = 80;        // швидкість ходьби, px/с
    private const double StepsPerSecond = 2;    // повних циклів кроку (обидві ноги) за секунду
    private const double HoldTime = 3;          // скільки мускот ходить з вкраденим текстом
    private const double CarriedTextScale = 0.6;

    // Видима частина мускота всередині контейнера 100x100 (за розмірами mascot.png)
    private const double MascotBodyLeft = 9;
    private const double MascotBodyCenter = 46;
    private const double MascotHeadTop = 18;

    private enum MascotState
    {
        Idle,           // стоїть вдома
        WalkToTitle,    // іде до напису
        Grab,           // хапає напис
        CarryHome,      // несе напис додому
        Hold,           // стоїть з написом на голові
        CarryBack,      // несе напис назад
        Release,        // повертає напис на місце
        WalkHome        // іде додому
    }

    private readonly TranslateTransform _leftPupilOffset = new();
    private readonly TranslateTransform _rightPupilOffset = new();
    private readonly ScaleTransform _mascotScale = new();
    private readonly TranslateTransform _mascotPosition = new();
    private readonly RotateTransform _mascotRotation = new();
    private readonly TranslateTransform _leftLegPosition = new();
    private readonly TranslateTransform _rightLegPosition = new();
    private readonly ScaleTransform _rightLegMirror = new()
    {
        ScaleX = -1
    };

    // Трансформації напису "Messages"
    private readonly ScaleTransform _titleScale = new();
    private readonly RotateTransform _titleRotation = new();
    private readonly TranslateTransform _titleOffset = new();

    private readonly Stopwatch _mascotClock = Stopwatch.StartNew();
    private double _jumpStart = -1;
    private readonly DispatcherTimer _mascotAnimation = new()
    {
        Interval = TimeSpan.FromMilliseconds(16)
    };

    // Стан "крадіжки"
    private MascotState _state = MascotState.Idle;
    private double _stateStart;
    private double _stateFrom;
    private double _stateTo;
    private double _stateDuration = double.PositiveInfinity;
    private double _lastTick;
    private double _textCarry;   // 0 = напис на місці, 1 = напис на голові мускота

    // Цикл ходьби
    private double _walkWeight;  // 0..1, плавно вмикає/вимикає ходьбу
    private double _walkPhase;
    private double _walkDir = -1;
    private double _walkBob;
    private double _walkTilt;
    private double _leftStepY;
    private double _rightStepY;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        LeftPupil.RenderTransform = _leftPupilOffset;
        RightPupil.RenderTransform = _rightPupilOffset;

        // Порядок важливий: спочатку масштаб і нахил, потім зсув,
        // інакше при ходьбі мускот "розгойдувався" б навколо старої позиції.
        MascotContainer.RenderTransform = new TransformGroup
        {
            Children = { _mascotScale, _mascotRotation, _mascotPosition }
        };
        LeftLeg.RenderTransform = _leftLegPosition;
        RightLeg.RenderTransform = new TransformGroup
        {
            Children = { _rightLegMirror, _rightLegPosition }
        };
        TitleText.RenderTransform = new TransformGroup
        {
            Children = { _titleScale, _titleRotation, _titleOffset }
        };

        _mascotScale.ScaleX = 1;
        _mascotScale.ScaleY = 1;
        _mascotAnimation.Tick += OnMascotAnimationTick;
        _mascotAnimation.Start();
        Closed += (_, _) => _mascotAnimation.Stop();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var cursor = e.GetPosition(MascotContainer);
        _leftPupilOffset.X = Math.Clamp((cursor.X - 33.1) / 75 * 2.8, -2.8, 2.8);
        _leftPupilOffset.Y = Math.Clamp((cursor.Y - 41.3) / 55 * 2.2, -2.2, 2.2);
        _rightPupilOffset.X = Math.Clamp((cursor.X - 60.7) / 75 * 2.8, -2.8, 2.8);
        _rightPupilOffset.Y = Math.Clamp((cursor.Y - 41.2) / 55 * 2.2, -2.2, 2.2);
    }

    private void OnMascotPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(MascotContainer).Properties;
        if (properties.IsRightButtonPressed && _state == MascotState.Idle)
        {
            _stateDuration = 0;
            e.Handled = true;
        }
        else if (properties.IsLeftButtonPressed)
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
        _mascotRotation.Angle = wobble + _walkTilt;

        var leftLegJump = GetDelayedJump(elapsed, 0.06);
        var rightLegJump = GetDelayedJump(elapsed, 0.3);
        _leftLegPosition.Y = -5 * leftLegJump + _leftStepY;
        _rightLegPosition.Y = -5 * rightLegJump + _rightStepY;

        UpdateTitle();
    }

    private double GetDelayedJump(double elapsed, double delay)
    {
        if (_jumpStart < 0)
        {
            return 0;
        }

        var legElapsed = elapsed - _jumpStart - delay;
        if (legElapsed < 0)
        {
            return 0;
        }

        if (legElapsed >= 0.9)
        {
            return 0;
        }

        return Math.Sin(legElapsed / 0.9 * Math.PI);
    }

    // ------------------------------------------------------------------
    // Сценарій: іде до напису -> хапає -> несе додому -> тримає на голові
    // -> несе назад -> кладе на місце -> іде додому -> чекає -> знову
    // ------------------------------------------------------------------
    private void UpdateMascotState(double now)
    {
        var t = now - _stateStart;

        switch (_state)
        {
            case MascotState.Idle:
                if (t >= _stateDuration)
                {
                    BeginMove(MascotState.WalkToTitle, GetTitleStopX(), now);
                }
                break;

            case MascotState.WalkToTitle:
                if (StepMove(t))
                {
                    BeginWait(MascotState.Grab, now, 0.6);
                    _jumpStart = now; // підстрибує, коли хапає напис
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
                    BeginWait(MascotState.Idle, now, double.PositiveInfinity);
                }
                break;
        }
    }

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

    // Повертає true, коли мускот дійшов до цілі.
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

    // Зсув мускота (відносно дому), при якому він стоїть біля правого краю напису.
    private double GetTitleStopX()
    {
        var mascot = MascotContainer.Bounds;
        var title = TitleText.Bounds;
        if (title.Width <= 0)
        {
            return 0;
        }

        return title.Right + 6 - (mascot.X + MascotBodyLeft);
    }

    // ------------------------------------------------------------------
    // Анімація ходьби: ноги по черзі підіймаються, тіло підстрибує і
    // перевалюється з боку в бік. _walkWeight плавно вмикає й вимикає це.
    // ------------------------------------------------------------------
    private void UpdateWalkCycle(double dt)
    {
        var target = IsWalking ? 1d : 0d;
        _walkWeight += (target - _walkWeight) * Math.Min(1, dt * 10);
        _walkPhase += dt * StepsPerSecond * Math.PI * 2 * _walkWeight;

        var sin = Math.Sin(_walkPhase);
        var cos = Math.Cos(_walkPhase);
        var w = _walkWeight;

        // Кожна нога підіймається лише у свою половину циклу
        _leftStepY = -5 * Math.Max(0, sin) * w;
        _rightStepY = -5 * Math.Max(0, -sin) * w;

        // Нога, що в повітрі, рухається вперед за напрямком ходьби
        _leftLegPosition.X = -cos * 3 * _walkDir * w;
        _rightLegPosition.X = cos * 3 * _walkDir * w;

        _walkBob = -2 * Math.Abs(sin) * w;
        _walkTilt = (3 * sin + 2 * _walkDir) * w;
    }

    // ------------------------------------------------------------------
    // Напис "Messages": лежить на місці (carry = 0) або сидить у мускота
    // на голові зменшений (carry = 1). Між ними летить дугою.
    // ------------------------------------------------------------------
    private void UpdateTitle()
    {
        var mascot = MascotContainer.Bounds;
        var title = TitleText.Bounds;
        if (title.Width <= 0)
        {
            return;
        }

        var p = _textCarry;
        var centerX = title.X + title.Width / 2;
        var centerY = title.Y + title.Height / 2;

        var targetX = mascot.X + _mascotPosition.X + MascotBodyCenter;
        var targetY = mascot.Y + MascotHeadTop - 5 + _mascotPosition.Y;

        var arc = -18 * Math.Sin(Math.PI * p); // дуга вгору під час перельоту

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
