using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WdmmDesktop.Generated;
using WdmmDesktop.Models;

namespace WdmmDesktop.Pages
{
    public sealed partial class TasksPage : Page
    {
        private List<QueueTask> _tasks = new();

        public TasksPage() => InitializeComponent();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Refresh();
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => Refresh();

        private void Refresh()
        {
            try
            {
                var json = NativeMethods.QueueStatusJson();
                _tasks = JsonSerializer.Deserialize<List<QueueTask>>(json)
                         ?? new List<QueueTask>();
            }
            catch
            {
                _tasks = new List<QueueTask>();
            }

            ApplyList(_tasks);
        }

        private void ApplyList(List<QueueTask> tasks)
        {
            TaskList.Items.Clear();

            int pending = 0;
            foreach (var t in tasks)
            {
                TaskList.Items.Add(t);
                if (t.Status is "pending" or "running") pending++;
            }

            if (tasks.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                StatusBar.Text        = "";
            }
            else
            {
                EmptyState.Visibility = Visibility.Collapsed;
                StatusBar.Text = $"{tasks.Count} task{(tasks.Count == 1 ? "" : "s")}";
            }

            if (pending > 0)
            {
                PendingBadge.Visibility = Visibility.Visible;
                PendingCount.Text       = pending.ToString();
            }
            else
            {
                PendingBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is QueueTask task && task.CanCancel)
            {
                try
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(task.Id + '\0');
                    var ptr   = Marshal.AllocHGlobal(bytes.Length);
                    try
                    {
                        Marshal.Copy(bytes, 0, ptr, bytes.Length);
                        NativeMethods.wdmm_queue_cancel(ptr).ToStringAndFree();
                    }
                    finally { Marshal.FreeHGlobal(ptr); }
                }
                catch { /* FFI not loaded — no-op */ }

                // Re-load after cancel so the list reflects the new state.
                Refresh();
            }
        }
    }
}
