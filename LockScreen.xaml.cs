using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Paperhome.Services;
using WpfColor = System.Windows.Media.Color;

namespace Paperhome
{
    public partial class LockScreen
    {
        public event EventHandler? Unlocked;

        private string _pin        = "";
        private string _confirmPin = "";
        private bool _confirmPhase;
        private bool _isSetupMode;
        private readonly AppSettings _settings;
        private readonly System.Windows.Shapes.Rectangle[] _dots;

        public LockScreen()
        {
            InitializeComponent();
            _settings    = AppSettings.Load();
            _dots        = [Dot1, Dot2, Dot3, Dot4];
            _isSetupMode = !EncryptionService.Current.HasPassword(_settings);

            TxtStatus.Text = _isSetupMode
                ? "> УСТАНОВИТЕ КОД ДОСТУПА"
                : "> ВВЕДИТЕ КОД ДОСТУПА";

            Loaded += (_, _) => Focus();
            KeyDown += OnKeyDown;
        }

        // ── Ввод с клавиатуры ────────────────────────────────────────────────

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key >= System.Windows.Input.Key.D0 &&
                e.Key <= System.Windows.Input.Key.D9)
                AppendDigit((e.Key - System.Windows.Input.Key.D0).ToString());
            else if (e.Key >= System.Windows.Input.Key.NumPad0 &&
                     e.Key <= System.Windows.Input.Key.NumPad9)
                AppendDigit((e.Key - System.Windows.Input.Key.NumPad0).ToString());
            else if (e.Key == System.Windows.Input.Key.Back)
                Backspace();
            else if (e.Key == System.Windows.Input.Key.Enter)
                _ = ConfirmAsync();
        }

        // ── Логика пина ──────────────────────────────────────────────────────

        private void AppendDigit(string digit)
        {
            if (_pin.Length >= 4) return;
            _pin += digit;
            UpdateDots();
        }

        private void Backspace()
        {
            if (_pin.Length == 0) return;
            _pin = _pin[..^1];
            UpdateDots();
        }

        private void UpdateDots()
        {
            for (int i = 0; i < 4; i++)
                _dots[i].Fill = new SolidColorBrush(
                    i < _pin.Length
                        ? WpfColor.FromRgb(0x60, 0xC0, 0x40)
                        : WpfColor.FromRgb(0x2A, 0x2A, 0x2A));
        }

        private async Task ConfirmAsync()
        {
            if (_pin.Length < 4) return;

            if (_isSetupMode)
            {
                if (!_confirmPhase)
                {
                    _confirmPin   = _pin;
                    _pin          = "";
                    _confirmPhase = true;
                    UpdateDots();
                    TxtStatus.Text = "> ПОДТВЕРДИТЕ КОД";
                }
                else if (_pin == _confirmPin)
                {
                    TxtStatus.Text = "> ПРИМЕНЯЮ...";
                    var pin = _pin;
                    await Task.Run(() => EncryptionService.Current.SetPassword(pin, _settings));
                    TxtStatus.Text = "> КОД УСТАНОВЛЕН";
                    Unlocked?.Invoke(this, EventArgs.Empty);
                    AnimateOpen();
                }
                else
                {
                    TxtStatus.Text = "> КОДЫ НЕ СОВПАДАЮТ";
                    _pin = ""; _confirmPin = ""; _confirmPhase = false;
                    UpdateDots();
                    Shake();
                }
            }
            else
            {
                TxtStatus.Text = "> ПРОВЕРКА...";
                var pin = _pin;
                bool ok = await Task.Run(() => EncryptionService.Current.TryUnlock(pin, _settings));

                if (ok)
                {
                    TxtStatus.Text = "> ДОСТУП РАЗРЕШЁН";
                    Unlocked?.Invoke(this, EventArgs.Empty);
                    AnimateOpen();
                }
                else
                {
                    TxtStatus.Text = "> НЕВЕРНЫЙ КОД";
                    _pin = "";
                    UpdateDots();
                    Shake();
                }
            }
        }

        // ── Анимации ─────────────────────────────────────────────────────────

        // Внешний вызов: заблокировать — шторы резко падают сверху
        public void Lock()
        {
            _pin          = "";
            _confirmPin   = "";
            _confirmPhase = false;
            _isSetupMode  = false; // пароль уже установлен
            UpdateDots();
            TxtStatus.Text = "> ВВЕДИТЕ КОД ДОСТУПА";

            // Снять предыдущую анимацию и поставить выше экрана
            ShutterTransform.BeginAnimation(TranslateTransform.YProperty, null);
            ShutterTransform.Y = -(ActualHeight + 20);
            Visibility         = Visibility.Visible;

            var anim = new DoubleAnimation
            {
                From           = -(ActualHeight + 20),
                To             = 0,
                Duration       = TimeSpan.FromMilliseconds(320),
                EasingFunction = new BounceEase { Bounces = 1, Bounciness = 4, EasingMode = EasingMode.EaseOut }
            };
            ShutterTransform.BeginAnimation(TranslateTransform.YProperty, anim);
        }

        private void AnimateOpen()
        {
            var anim = new DoubleAnimation
            {
                To             = -(ActualHeight + 100),
                Duration       = TimeSpan.FromMilliseconds(560),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (_, _) => Visibility = Visibility.Collapsed;
            ShutterTransform.BeginAnimation(TranslateTransform.YProperty, anim);
        }

        private void Shake()
        {
            var anim = new DoubleAnimationUsingKeyFrames();
            void F(double x, int ms) => anim.KeyFrames.Add(
                new LinearDoubleKeyFrame(x, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
            F(-12, 55); F(12, 110); F(-9, 170); F(9, 230); F(-5, 290); F(0, 350);
            ShakeTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        // ── Кнопки ───────────────────────────────────────────────────────────

        private void PinKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button { Tag: string d }) AppendDigit(d);
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)    => Backspace();
        private void BtnConfirm_Click(object sender, RoutedEventArgs e) => _ = ConfirmAsync();
    }
}
