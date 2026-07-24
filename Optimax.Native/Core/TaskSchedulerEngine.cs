using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Optimax.Core
{
    public static class TaskSchedulerEngine
    {
        private const string TASK_NAME = "OptimaxAutoClean";

        [DllImport("ole32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern int CoCreateInstance(
            [In] ref Guid rclsid,
            IntPtr pUnkOuter,
            uint dwClsContext,
            [In] ref Guid riid,
            [Out, MarshalAs(UnmanagedType.Interface)] out object ppv);

        private static readonly Guid CLSID_TaskScheduler = new Guid("0F87369F-A4E5-4CFC-BD3E-73E6154572DD");
        private static readonly Guid IID_ITaskService = new Guid("2F544C3D-0635-423D-934C-F20396146893");
        private const uint CLSCTX_INPROC_SERVER = 1;

        private static ITaskService CreateTaskService()
        {
            Guid clsid = CLSID_TaskScheduler;
            Guid iid = IID_ITaskService;
            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out object? obj);
            if (hr < 0 || obj == null) Marshal.ThrowExceptionForHR(hr);
            return (ITaskService)obj;
        }

        public static bool ScheduleDaily(TimeSpan time, bool isDryRun = true)
        {
            try
            {
                var taskService = CreateTaskService();
                taskService.Connect(null, null, null, null);

                ITaskFolder rootFolder = taskService.GetFolder("\\");
                ITaskDefinition taskDef = taskService.NewTask(0);

                // Registration / Principal Info
                IPrincipal principal = taskDef.Principal;
                principal.RunLevel = _TASK_RUNLEVEL.TASK_RUNLEVEL_HIGHEST;
                principal.LogonType = _TASK_LOGON_TYPE.TASK_LOGON_SERVICE_ACCOUNT;
                principal.UserId = "SYSTEM";

                // Action
                IActionCollection actions = taskDef.Actions;
                IExecAction action = (IExecAction)actions.Create(_TASK_ACTION_TYPE.TASK_ACTION_EXEC);
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Optimax.exe");
                action.Path = exePath;
                action.Arguments = isDryRun ? "--dry-run" : "--clean-registry";

                // Trigger (Daily)
                ITriggerCollection triggers = taskDef.Triggers;
                IDailyTrigger trigger = (IDailyTrigger)triggers.Create(_TASK_TRIGGER_TYPE2.TASK_TRIGGER_DAILY);
                DateTime now = DateTime.Now;
                DateTime start = new DateTime(now.Year, now.Month, now.Day, time.Hours, time.Minutes, 0);
                if (start < now) start = start.AddDays(1);
                trigger.StartBoundary = start.ToString("yyyy-MM-ddTHH:mm:ss");
                trigger.DaysInterval = 1;

                // Settings
                ITaskSettings settings = taskDef.Settings;
                settings.DisallowStartIfOnBatteries = false;
                settings.StopIfGoingOnBatteries = false;
                settings.ExecutionTimeLimit = "PT1H";

                rootFolder.RegisterTaskDefinition(
                    TASK_NAME,
                    taskDef,
                    (int)_TASK_CREATION.TASK_CREATE_OR_UPDATE,
                    "SYSTEM",
                    null,
                    _TASK_LOGON_TYPE.TASK_LOGON_SERVICE_ACCOUNT,
                    null);

                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TASK SCHEDULER ERROR] Failed to schedule daily task: {ex.Message}");
                return false;
            }
        }

        public static bool ScheduleWeekly(DayOfWeek dayOfWeek, TimeSpan time, bool isDryRun = true)
        {
            try
            {
                var taskService = CreateTaskService();
                taskService.Connect(null, null, null, null);

                ITaskFolder rootFolder = taskService.GetFolder("\\");
                ITaskDefinition taskDef = taskService.NewTask(0);

                IPrincipal principal = taskDef.Principal;
                principal.RunLevel = _TASK_RUNLEVEL.TASK_RUNLEVEL_HIGHEST;
                principal.LogonType = _TASK_LOGON_TYPE.TASK_LOGON_SERVICE_ACCOUNT;
                principal.UserId = "SYSTEM";

                IActionCollection actions = taskDef.Actions;
                IExecAction action = (IExecAction)actions.Create(_TASK_ACTION_TYPE.TASK_ACTION_EXEC);
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Optimax.exe");
                action.Path = exePath;
                action.Arguments = isDryRun ? "--dry-run" : "--clean-registry";

                ITriggerCollection triggers = taskDef.Triggers;
                IWeeklyTrigger trigger = (IWeeklyTrigger)triggers.Create(_TASK_TRIGGER_TYPE2.TASK_TRIGGER_WEEKLY);
                DateTime now = DateTime.Now;
                DateTime start = new DateTime(now.Year, now.Month, now.Day, time.Hours, time.Minutes, 0);
                trigger.StartBoundary = start.ToString("yyyy-MM-ddTHH:mm:ss");
                trigger.WeeksInterval = 1;

                short daysOfWeekFlag = dayOfWeek switch
                {
                    DayOfWeek.Sunday => 1,
                    DayOfWeek.Monday => 2,
                    DayOfWeek.Tuesday => 4,
                    DayOfWeek.Wednesday => 8,
                    DayOfWeek.Thursday => 16,
                    DayOfWeek.Friday => 32,
                    DayOfWeek.Saturday => 64,
                    _ => 1
                };
                trigger.DaysOfWeek = daysOfWeekFlag;

                ITaskSettings settings = taskDef.Settings;
                settings.DisallowStartIfOnBatteries = false;
                settings.StopIfGoingOnBatteries = false;

                rootFolder.RegisterTaskDefinition(
                    TASK_NAME,
                    taskDef,
                    (int)_TASK_CREATION.TASK_CREATE_OR_UPDATE,
                    "SYSTEM",
                    null,
                    _TASK_LOGON_TYPE.TASK_LOGON_SERVICE_ACCOUNT,
                    null);

                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TASK SCHEDULER ERROR] Failed to schedule weekly task: {ex.Message}");
                return false;
            }
        }

        public static bool Unschedule()
        {
            try
            {
                var taskService = CreateTaskService();
                taskService.Connect(null, null, null, null);

                ITaskFolder rootFolder = taskService.GetFolder("\\");
                rootFolder.DeleteTask(TASK_NAME, 0);
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TASK SCHEDULER ERROR] Failed to unschedule task: {ex.Message}");
                return false;
            }
        }


        #region COM Interfaces Declarations

        private enum _TASK_TRIGGER_TYPE2
        {
            TASK_TRIGGER_DAILY = 2,
            TASK_TRIGGER_WEEKLY = 3
        }

        private enum _TASK_ACTION_TYPE
        {
            TASK_ACTION_EXEC = 0
        }

        private enum _TASK_CREATION
        {
            TASK_CREATE_OR_UPDATE = 6
        }

        private enum _TASK_LOGON_TYPE
        {
            TASK_LOGON_SERVICE_ACCOUNT = 5
        }

        private enum _TASK_RUNLEVEL
        {
            TASK_RUNLEVEL_HIGHEST = 1
        }

        [ComImport]
        [Guid("2F544C3D-0635-423D-934C-F20396146893")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface ITaskService
        {
            [return: MarshalAs(UnmanagedType.Interface)]
            ITaskFolder GetFolder([In, MarshalAs(UnmanagedType.BStr)] string Path);
            void Connect([In, Optional, MarshalAs(UnmanagedType.Struct)] object? ServerName, [In, Optional, MarshalAs(UnmanagedType.Struct)] object? User, [In, Optional, MarshalAs(UnmanagedType.Struct)] object? Domain, [In, Optional, MarshalAs(UnmanagedType.Struct)] object? Password);
            [return: MarshalAs(UnmanagedType.Interface)]
            ITaskDefinition NewTask([In] uint flags);
        }

        [ComImport]
        [Guid("829D0036-457A-485D-A6A2-7086163123A1")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface ITaskFolder
        {
            [return: MarshalAs(UnmanagedType.Interface)]
            object RegisterTaskDefinition([In, MarshalAs(UnmanagedType.BStr)] string Path, [In, MarshalAs(UnmanagedType.Interface)] ITaskDefinition pDefinition, [In] int flags, [In, Optional, MarshalAs(UnmanagedType.Struct)] object UserId, [In, Optional, MarshalAs(UnmanagedType.Struct)] object? password, [In] _TASK_LOGON_TYPE logonType, [In, Optional, MarshalAs(UnmanagedType.Struct)] object? sddl);
            void DeleteTask([In, MarshalAs(UnmanagedType.BStr)] string Name, [In] int flags);
        }

        [ComImport]
        [Guid("F5E8C999-44E2-4FEC-B449-D17E72FA3C33")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface ITaskDefinition
        {
            [property: DispId(1)]
            ITriggerCollection Triggers { get; }
            [property: DispId(3)]
            IPrincipal Principal { get; }
            [property: DispId(4)]
            IActionCollection Actions { get; }
            [property: DispId(5)]
            ITaskSettings Settings { get; }
        }

        [ComImport]
        [Guid("85B4E0B0-73A4-4696-A0D9-012482C5E782")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface ITriggerCollection
        {
            [return: MarshalAs(UnmanagedType.Interface)]
            ITrigger Create([In] _TASK_TRIGGER_TYPE2 type);
        }

        [ComImport]
        [Guid("0994B819-4B5F-485B-9A44-31014205B910")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface ITrigger
        {
            [property: DispId(3)]
            string StartBoundary { set; }
        }

        [ComImport]
        [Guid("126C5CD8-B288-41D5-8E7E-F615953017CA")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IDailyTrigger : ITrigger
        {
            [property: DispId(3)]
            new string StartBoundary { set; }
            [property: DispId(5)]
            short DaysInterval { set; }
        }

        [ComImport]
        [Guid("5038FC98-82FF-436D-8728-A512A57C9DC0")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IWeeklyTrigger : ITrigger
        {
            [property: DispId(3)]
            new string StartBoundary { set; }
            [property: DispId(5)]
            short DaysOfWeek { set; }
            [property: DispId(6)]
            short WeeksInterval { set; }
        }

        [ComImport]
        [Guid("02820E19-7B98-4DA2-B83C-258D76A3D109")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IActionCollection
        {
            [return: MarshalAs(UnmanagedType.Interface)]
            IAction Create([In] _TASK_ACTION_TYPE type);
        }

        [ComImport]
        [Guid("BAE5499F-88B5-474B-8100-D26B5D467654")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IAction
        {
        }

        [ComImport]
        [Guid("4C3D624D-FD6B-49A3-B950-8AE246E03724")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IExecAction : IAction
        {
            [property: DispId(1)]
            string Path { set; }
            [property: DispId(2)]
            string Arguments { set; }
        }

        [ComImport]
        [Guid("D98D6995-E598-457A-BC72-468C79545D99")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IPrincipal
        {
            [property: DispId(1)]
            string UserId { set; }
            [property: DispId(2)]
            _TASK_LOGON_TYPE LogonType { set; }
            [property: DispId(4)]
            _TASK_RUNLEVEL RunLevel { set; }
        }

        [ComImport]
        [Guid("8FD4711D-2D02-43B6-B68A-821A79B35CDE")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface ITaskSettings
        {
            [property: DispId(5)]
            bool DisallowStartIfOnBatteries { set; }
            [property: DispId(6)]
            bool StopIfGoingOnBatteries { set; }
            [property: DispId(14)]
            string ExecutionTimeLimit { set; }
        }

        #endregion
    }
}
