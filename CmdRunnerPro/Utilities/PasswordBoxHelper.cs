
using System.Windows;
using System.Windows.Controls;

namespace CmdRunnerPro.Utilities
{
    public static class PasswordBoxHelper
    {
        public static readonly DependencyProperty BoundPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BoundPassword",
                typeof(string),
                typeof(PasswordBoxHelper),
                new FrameworkPropertyMetadata(string.Empty, OnBoundPasswordChanged));

        public static readonly DependencyProperty BindPasswordProperty =
            DependencyProperty.RegisterAttached(
                "BindPassword",
                typeof(bool),
                typeof(PasswordBoxHelper),
                new PropertyMetadata(false, OnBindPasswordChanged));

        private static readonly DependencyProperty IsUpdatingProperty =
            DependencyProperty.RegisterAttached(
                "IsUpdating",
                typeof(bool),
                typeof(PasswordBoxHelper),
                new PropertyMetadata(false));

        public static string GetBoundPassword(DependencyObject dp) =>
            (string)dp.GetValue(BoundPasswordProperty);

        public static void SetBoundPassword(DependencyObject dp, string value) =>
            dp.SetValue(BoundPasswordProperty, value);

        public static bool GetBindPassword(DependencyObject dp) =>
            (bool)dp.GetValue(BindPasswordProperty);

        public static void SetBindPassword(DependencyObject dp, bool value) =>
            dp.SetValue(BindPasswordProperty, value);

        private static bool GetIsUpdating(DependencyObject dp) =>
            (bool)dp.GetValue(IsUpdatingProperty);

        private static void SetIsUpdating(DependencyObject dp, bool value) =>
            dp.SetValue(IsUpdatingProperty, value);

        private static void OnBoundPasswordChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            if (dp is not PasswordBox pb) return;

            pb.PasswordChanged -= HandlePasswordChanged;

            if (!GetIsUpdating(pb))
                pb.Password = e.NewValue as string ?? string.Empty;

            pb.PasswordChanged += HandlePasswordChanged;
        }

        private static void OnBindPasswordChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            if (dp is not PasswordBox pb) return;

            bool wasBound = (bool)e.OldValue;
            bool needBind = (bool)e.NewValue;

            if (wasBound)
                pb.PasswordChanged -= HandlePasswordChanged;

            if (needBind)
                pb.PasswordChanged += HandlePasswordChanged;
        }

        private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not PasswordBox pb) return;

            SetIsUpdating(pb, true);
            SetBoundPassword(pb, pb.Password);
            SetIsUpdating(pb, false);
        }
    }
}
