namespace PlaymoxWeb.Services;

/// <summary>
/// Strongly-typed localization keys providing compile-time safety and IDE IntelliSense.
/// All localized values are loaded from wwwroot/locales/{lang}.json.
/// </summary>
public static class Tr
{
    public static class Common
    {
        /// <summary>Cancel</summary>
        public const string Cancel = "common.cancel";
        /// <summary>Close</summary>
        public const string Close = "common.close";
        /// <summary>Save</summary>
        public const string Save = "common.save";
        /// <summary>Delete</summary>
        public const string Delete = "common.delete";
        /// <summary>Search...</summary>
        public const string Search = "common.search";
        /// <summary>Actions</summary>
        public const string Actions = "common.actions";
        /// <summary>Status</summary>
        public const string Status = "common.status";
        /// <summary>Error</summary>
        public const string Error = "common.error";
        /// <summary>Yes</summary>
        public const string Yes = "common.yes";
        /// <summary>No</summary>
        public const string No = "common.no";
        /// <summary>Copy</summary>
        public const string Copy = "common.copy";
        /// <summary>Back</summary>
        public const string Back = "common.back";
        /// <summary>Next</summary>
        public const string Next = "common.next";
        /// <summary>Finish</summary>
        public const string Finish = "common.finish";
        /// <summary>Skip</summary>
        public const string Skip = "common.skip";
        /// <summary>Please log in to the panel first.</summary>
        public const string PleaseLoginFirst = "common.please_login_first";
        /// <summary>Parameter</summary>
        public const string Parameter = "common.parameter";
        /// <summary>Value</summary>
        public const string Value = "common.value";
        /// <summary>Add parameter</summary>
        public const string AddParameter = "common.add_parameter";
        /// <summary>Additional Parameters (optional)</summary>
        public const string AdditionalParamsOptional = "common.additional_params_optional";
        /// <summary>Advanced keys that will be passed directly to the game configuration.</summary>
        public const string AdditionalParamsHelper = "common.additional_params_helper";
        /// <summary>Disk (GB)</summary>
        public const string DiskGb = "common.disk_gb";
        /// <summary>Public SSH</summary>
        public const string PublicSsh = "common.public_ssh";
        /// <summary>Failed to read local session: {0}</summary>
        public const string SessionReadFailed = "common.session_read_failed";
        /// <summary>File download timed out.</summary>
        public const string FileDownloadTimedOut = "common.file_download_timed_out";
        /// <summary>Failed to download file: {0}</summary>
        public const string FileDownloadFailed = "common.file_download_failed";
        /// <summary>File upload timed out.</summary>
        public const string FileUploadTimedOut = "common.file_upload_timed_out";
        /// <summary>Failed to upload file: {0}</summary>
        public const string FileUploadFailed = "common.file_upload_failed";
        /// <summary>Session expired. Please log in again.</summary>
        public const string SessionExpired = "common.session_expired";
        /// <summary>API Error ({0})</summary>
        public const string ApiErrorStatus = "common.api_error_status";
        /// <summary>CPU</summary>
        public const string Cpu = "common.cpu";
        /// <summary>RAM</summary>
        public const string Ram = "common.ram";
        /// <summary>RAM (MB)</summary>
        public const string RamMb = "common.ram_mb";
    }

    public static class App
    {
        /// <summary>Not found</summary>
        public const string NotFound = "app.not_found";
        /// <summary>Sorry, there's nothing at this address.</summary>
        public const string NotFoundMessage = "app.not_found_message";
    }

    public static class Nav
    {
        /// <summary>Toggle theme</summary>
        public const string ToggleTheme = "nav.toggle_theme";
        /// <summary>Tutorial</summary>
        public const string Tutorial = "nav.tutorial";
        /// <summary>Log out</summary>
        public const string LogOut = "nav.logout";
        /// <summary>Log in</summary>
        public const string LogIn = "nav.login";
        /// <summary>Control Panel</summary>
        public const string ControlPanel = "nav.control_panel";
        /// <summary>Dashboard</summary>
        public const string Dashboard = "nav.dashboard";
        /// <summary>ADMINISTRATION</summary>
        public const string AdministrationSection = "nav.admin_section";
        /// <summary>All Servers</summary>
        public const string AllServers = "nav.all_servers";
        /// <summary>Users</summary>
        public const string Users = "nav.users";
        /// <summary>Configuration</summary>
        public const string Configuration = "nav.configuration";
        /// <summary>GAME SERVERS</summary>
        public const string GameServersSection = "nav.game_servers_section";
        /// <summary>My Servers</summary>
        public const string MyServers = "nav.my_servers";
    }

    public static class Login
    {
        /// <summary>Log in - Playmox</summary>
        public const string PageTitle = "login.page_title";
        /// <summary>Welcome to the Playmox panel</summary>
        public const string WelcomeSubtitle = "login.welcome_subtitle";
        /// <summary>Login</summary>
        public const string UsernameLabel = "login.username_label";
        /// <summary>Password</summary>
        public const string PasswordLabel = "login.password_label";
        /// <summary>Logging in...</summary>
        public const string LoggingIn = "login.logging_in";
        /// <summary>Login failed.</summary>
        public const string LoginFailed = "login.login_failed";
        /// <summary>Missing token in API response.</summary>
        public const string MissingTokenInResponse = "login.missing_token";
    }

    public static class Home
    {
        /// <summary>Dashboard - Playmox</summary>
        public const string PageTitle = "home.page_title";
        /// <summary>Welcome to Playmox</summary>
        public const string WelcomeTitle = "home.welcome_title";
        /// <summary>Log in to manage your game servers.</summary>
        public const string AnonymousPrompt = "home.anonymous_prompt";
        /// <summary>Administrator Dashboard</summary>
        public const string AdminDashboardTitle = "home.admin_dashboard_title";
        /// <summary>Refreshing...</summary>
        public const string Refreshing = "home.refreshing";
        /// <summary>Refresh data</summary>
        public const string RefreshData = "home.refresh_data";
        /// <summary>Proxmox Host</summary>
        public const string ProxmoxHost = "home.proxmox_host";
        /// <summary>CPU Usage</summary>
        public const string CpuUsage = "home.cpu_usage";
        /// <summary>Instances</summary>
        public const string Instances = "home.instances";
        /// <summary>Templates:</summary>
        public const string TemplatesLabel = "home.templates_label";
        /// <summary>API Status</summary>
        public const string ApiStatus = "home.api_status";
        /// <summary>Connected</summary>
        public const string Connected = "home.connected";
        /// <summary>Host RAM Memory</summary>
        public const string HostRamMemory = "home.host_ram_memory";
        /// <summary>Host RootFS Disk</summary>
        public const string HostRootfsDisk = "home.host_rootfs_disk";
        /// <summary>Storage Resources</summary>
        public const string StorageResources = "home.storage_resources";
        /// <summary>Name</summary>
        public const string StorageName = "home.storage_name";
        /// <summary>Type</summary>
        public const string StorageType = "home.storage_type";
        /// <summary>Active</summary>
        public const string StorageActive = "home.storage_active";
        /// <summary>Inactive</summary>
        public const string StorageInactive = "home.storage_inactive";
        /// <summary>Usage</summary>
        public const string StorageUsage = "home.storage_usage";
        /// <summary>You are logged in. Manage your servers in the "My Servers" section.</summary>
        public const string UserLoggedInBanner = "home.user_logged_in_banner";
        /// <summary>Manage your VM instances</summary>
        public const string ManageInstancesDesc = "home.manage_instances_desc";
        /// <summary>Settings</summary>
        public const string SettingsTitle = "home.settings_title";
        /// <summary>Account and profile configuration</summary>
        public const string SettingsDesc = "home.settings_desc";
        /// <summary>Dashboard refreshed.</summary>
        public const string DashboardRefreshed = "home.dashboard_refreshed";
    }

    public static class Instances
    {
        /// <summary>All Servers - Playmox</summary>
        public const string PageTitleAdmin = "instances.page_title_admin";
        /// <summary>My Servers - Playmox</summary>
        public const string PageTitleUser = "instances.page_title_user";
        /// <summary>Create VM</summary>
        public const string CreateVm = "instances.create_vm";
        /// <summary>CPU (Host Cores)</summary>
        public const string HostCpuCores = "instances.host_cpu_cores";
        /// <summary>Allocated cores across active VMs / Total physical host cores in Proxmox.</summary>
        public const string TooltipHostCpu = "instances.tooltip_host_cpu";
        /// <summary>Host RAM</summary>
        public const string HostRam = "instances.host_ram";
        /// <summary>Total host RAM used / available in Proxmox.</summary>
        public const string TooltipHostRam = "instances.tooltip_host_ram";
        /// <summary>Total VMs</summary>
        public const string TotalVms = "instances.total_vms";
        /// <summary>Active VMs in Playmox and total VMs on Proxmox node.</summary>
        public const string TooltipTotalVms = "instances.tooltip_total_vms";
        /// <summary>Host Disk</summary>
        public const string HostDisk = "instances.host_disk";
        /// <summary>Root storage used / total on Proxmox host.</summary>
        public const string TooltipHostDisk = "instances.tooltip_host_disk";
        /// <summary>Public ports</summary>
        public const string PublicPorts = "instances.public_ports";
        /// <summary>Total active public ports allocated across game servers.</summary>
        public const string TooltipHostPorts = "instances.tooltip_host_ports";
        /// <summary>active</summary>
        public const string ActivePortsSuffix = "instances.active_ports_suffix";
        /// <summary>CPU (cores)</summary>
        public const string QuotaCpuCores = "instances.quota_cpu_cores";
        /// <summary>Number of CPU cores allocated across your active game servers (VMs).</summary>
        public const string TooltipUserCpu = "instances.tooltip_user_cpu";
        /// <summary>Total amount of RAM in megabytes (MB) currently used by your active machines.</summary>
        public const string TooltipUserRam = "instances.tooltip_user_ram";
        /// <summary>Number of virtual machines currently running or being provisioned.</summary>
        public const string TooltipUserInstances = "instances.tooltip_user_instances";
        /// <summary>Total disk storage space allocated to your virtual machines in gigabytes (GB).</summary>
        public const string TooltipUserDisk = "instances.tooltip_user_disk";
        /// <summary>Number of available public game ports - each game server requires 1 or more ports.</summary>
        public const string TooltipUserPorts = "instances.tooltip_user_ports";
        /// <summary>Machine List</summary>
        public const string MachineList = "instances.machine_list";
        /// <summary>Show passwords</summary>
        public const string ShowPasswords = "instances.show_passwords";
        /// <summary>Show deleted</summary>
        public const string ShowDeleted = "instances.show_deleted";
        /// <summary>Owner</summary>
        public const string Owner = "instances.owner";
        /// <summary>Game / Name</summary>
        public const string GameAndName = "instances.game_and_name";
        /// <summary>Access Details</summary>
        public const string AccessDetails = "instances.access_details";
        /// <summary>Game Port:</summary>
        public const string GamePortLabel = "instances.game_port_label";
        /// <summary>SSH gives advanced users access to the server console, logs, and manual command execution.</summary>
        public const string TooltipSshAccess = "instances.tooltip_ssh_access";
        /// <summary>No login credentials</summary>
        public const string NoLoginCredentials = "instances.no_login_credentials";
        /// <summary>Game status</summary>
        public const string GameStatusButton = "instances.game_status_button";
        /// <summary>Status:</summary>
        public const string RuntimeStatusLabel = "instances.runtime_status_label";
        /// <summary>Listening</summary>
        public const string PortListening = "instances.port_listening";
        /// <summary>Listening...</summary>
        public const string PortListeningPending = "instances.port_listening_pending";
        /// <summary>Version:</summary>
        public const string VersionLabel = "instances.version_label";
        /// <summary>Players:</summary>
        public const string PlayersLabel = "instances.players_label";
        /// <summary>Last Action</summary>
        public const string LastAction = "instances.last_action";
        /// <summary>History</summary>
        public const string HistoryLink = "instances.history_link";
        /// <summary>Start</summary>
        public const string ActionStart = "instances.action_start";
        /// <summary>Restart</summary>
        public const string ActionRestart = "instances.action_restart";
        /// <summary>Shutdown</summary>
        public const string ActionShutdown = "instances.action_shutdown";
        /// <summary>Force Stop</summary>
        public const string ActionForceStop = "instances.action_force_stop";
        /// <summary>Game configuration</summary>
        public const string MenuGameConfig = "instances.menu_game_config";
        /// <summary>Console / Debug</summary>
        public const string MenuConsoleDebug = "instances.menu_console_debug";
        /// <summary>Download backup</summary>
        public const string MenuDownloadBackup = "instances.menu_download_backup";
        /// <summary>Restore backup</summary>
        public const string MenuRestoreBackup = "instances.menu_restore_backup";
        /// <summary>Edit resources</summary>
        public const string MenuEditResources = "instances.menu_edit_resources";
        /// <summary>Reinstall game</summary>
        public const string MenuReinstallGame = "instances.menu_reinstall_game";
        /// <summary>Reset SSH password</summary>
        public const string MenuResetSshPassword = "instances.menu_reset_ssh_password";
        /// <summary>Delete VM</summary>
        public const string MenuDeleteVm = "instances.menu_delete_vm";
        /// <summary>Debugging VM #{0}</summary>
        public const string DebuggingVmTitle = "instances.debugging_vm_title";
        /// <summary>Click here and type commands...</summary>
        public const string TerminalInputPlaceholder = "instances.terminal_input_placeholder";
        /// <summary>Edit Resources for VM #{0}</summary>
        public const string EditResourcesTitle = "instances.edit_resources_title";
        /// <summary>Display Name</summary>
        public const string DisplayName = "instances.display_name";
        /// <summary>Changing CPU/RAM requires a VM restart to take full effect.</summary>
        public const string EditResourcesRestartNote = "instances.edit_resources_restart_note";
        /// <summary>New Virtual Machine</summary>
        public const string NewVmDialogTitle = "instances.new_vm_dialog_title";
        /// <summary>Configuration saved. Restarting game...</summary>
        public const string ConfigSavedRestarting = "instances.config_saved_restarting";
        /// <summary>Operation queued: {0}</summary>
        public const string OperationQueued = "instances.operation_queued";
        /// <summary>Force Stop VM</summary>
        public const string ForceStopDialogTitle = "instances.force_stop_dialog_title";
        /// <summary>A background job is currently running or queued for this VM. Forcing a stop will immediately power off the VM and cancel the active job. Continue?</summary>
        public const string ForceStopConfirmMessage = "instances.force_stop_confirm_message";
        /// <summary>VM has been forcibly stopped.</summary>
        public const string VmForciblyStopped = "instances.vm_forcibly_stopped";
        /// <summary>Are you sure you want to delete this virtual machine? All data will be permanently lost.</summary>
        public const string DeleteVmConfirmMessage = "instances.delete_vm_confirm_message";
        /// <summary>VM deletion has been queued.</summary>
        public const string VmDeletionQueued = "instances.vm_deletion_queued";
        /// <summary>SSH password reset has been queued.</summary>
        public const string PasswordResetQueued = "instances.password_reset_queued";
        /// <summary>Resources updated. A VM restart is required.</summary>
        public const string ResourcesUpdated = "instances.resources_updated";
        /// <summary>Job History</summary>
        public const string JobHistoryDialogTitle = "instances.job_history_dialog_title";
        /// <summary>Connecting...</summary>
        public const string SshConnecting = "instances.ssh_connecting";
        /// <summary>Failed to obtain a valid SSH session. Please log in again.</summary>
        public const string SshSessionFailed = "instances.ssh_session_failed";
        /// <summary>Active: {0}</summary>
        public const string StreamActive = "instances.stream_active";
        /// <summary>No active operation.</summary>
        public const string StreamNoActiveOperation = "instances.stream_no_active_operation";
        /// <summary>Disconnected: </summary>
        public const string SshDisconnectedPrefix = "instances.ssh_disconnected_prefix";
        /// <summary>SSH error: </summary>
        public const string SshErrorPrefix = "instances.ssh_error_prefix";
        /// <summary>Connected.</summary>
        public const string SshConnected = "instances.ssh_connected";
        /// <summary>SSH terminal is disconnected. Reopen the console.</summary>
        public const string SshTerminalDisconnected = "instances.ssh_terminal_disconnected";
        /// <summary>Download started.</summary>
        public const string BackupDownloadStarted = "instances.backup_download_started";
        /// <summary>Backup restored. Restarting game...</summary>
        public const string BackupRestoredRestarting = "instances.backup_restored_restarting";
        /// <summary>Reinstall Game</summary>
        public const string ReinstallDialogTitle = "instances.reinstall_dialog_title";
        /// <summary>Are you sure you want to reinstall the game? Configuration and world data may be lost.</summary>
        public const string ReinstallConfirmMessage = "instances.reinstall_confirm_message";
        /// <summary>Game reinstallation queued.</summary>
        public const string ReinstallQueued = "instances.reinstall_queued";
        /// <summary>Host</summary>
        public const string HostLabel = "instances.host_label";
        /// <summary>SSH Port</summary>
        public const string SshPortLabel = "instances.ssh_port_label";
        /// <summary>API Command Stream</summary>
        public const string TabApiCommandStream = "instances.tab_api_command_stream";
        /// <summary>SSH Terminal</summary>
        public const string TabSshTerminal = "instances.tab_ssh_terminal";
        /// <summary>Auto-scroll</summary>
        public const string AutoScroll = "instances.auto_scroll";
    }

    public static class Tutorial
    {
        /// <summary>Welcome to the Playmox tutorial! You are looking at 3 sample VMs and demo resource limits. This preview does not modify your data — it helps you quickly understand the panel.</summary>
        public const string WelcomeBody = "tutorial.welcome_body";
        /// <summary>Your Resource Limits</summary>
        public const string LimitsTitle = "tutorial.limits_title";
        /// <summary>CPU, RAM, and instance count only count when a VM is running or being created/updated. A stopped VM does not consume compute limits. &lt;br/&gt;&lt;br/&gt;Instance count defines how many VMs you can have running simultaneously.</summary>
        public const string LimitsBody = "tutorial.limits_body";
        /// <summary>Disk</summary>
        public const string DiskTitle = "tutorial.disk_title";
        /// <summary>Disk usage is counted as the total allocated VM storage capacity, regardless of actual usage. It reserves space on the host for your machine.</summary>
        public const string DiskBody = "tutorial.disk_body";
        /// <summary>Public Ports</summary>
        public const string PublicPortsTitle = "tutorial.public_ports_title";
        /// <summary>Public ports are counted only for active instances. Each game server consumes a specific number of ports shown during VM creation (SSH ports do not count toward the limit).</summary>
        public const string PublicPortsBody = "tutorial.public_ports_body";
        /// <summary>Each row represents a separate game server. You can see its status, resources, and live game runtime information. The status shows whether the VM is running or applying changes.</summary>
        public const string MachineRowBody = "tutorial.machine_row_body";
        /// <summary>Host is the address you enter in your game client. SSH is a secure administrative console channel — useful for advanced users to inspect logs or manually manage the machine.</summary>
        public const string AccessDetailsBody = "tutorial.access_details_body";
        /// <summary>Quick Actions</summary>
        public const string QuickActionsTitle = "tutorial.quick_actions_title";
        /// <summary>Start, restart, graceful shutdown, and force stop — the core controls for your server.</summary>
        public const string QuickActionsBody = "tutorial.quick_actions_body";
        /// <summary>More Actions</summary>
        public const string MoreActionsTitle = "tutorial.more_actions_title";
        /// <summary>The "three dots" menu contains advanced actions: game configuration, console/SSH, backups, reinstallation, resource management, and access credentials.</summary>
        public const string MoreActionsBody = "tutorial.more_actions_body";
        /// <summary>Creating a VM</summary>
        public const string CreateVmTitle = "tutorial.create_vm_title";
        /// <summary>Here you choose a game and resources, and Playmox automatically provisions the machine and installs the server.</summary>
        public const string CreateVmBody = "tutorial.create_vm_body";
        /// <summary>Game &amp; Name Selection</summary>
        public const string GameSelectionTitle = "tutorial.game_selection_title";
        /// <summary>Select a game (e.g. Minecraft) and an optional VM name. The name helps distinguish your servers on the list.</summary>
        public const string GameSelectionBody = "tutorial.game_selection_body";
        /// <summary>VM Resources</summary>
        public const string VmResourcesTitle = "tutorial.vm_resources_title";
        /// <summary>CPU, RAM, and disk are hard VM limits. They affect performance and how many servers you can run simultaneously within your quota.</summary>
        public const string VmResourcesBody = "tutorial.vm_resources_body";
        /// <summary>Game Configuration</summary>
        public const string GameConfigTitle = "tutorial.game_config_title";
        /// <summary>Game-specific parameters (e.g. server version, max players, world mode). Settings are saved and automatically applied.</summary>
        public const string GameConfigBody = "tutorial.game_config_body";
        /// <summary>Additional Parameters</summary>
        public const string AdditionalParamsTitle = "tutorial.additional_params_title";
        /// <summary>Advanced parameters are passed directly into the game configuration. Use them if you need custom settings for a specific game server.</summary>
        public const string AdditionalParamsBody = "tutorial.additional_params_body";
        /// <summary>Launch</summary>
        public const string LaunchTitle = "tutorial.launch_title";
        /// <summary>After creating the VM, Playmox starts background installation. The server will appear on the list, and its status will indicate when it is ready. Provisioning and installing components may take a few minutes.</summary>
        public const string LaunchBody = "tutorial.launch_body";
    }

    public static class CreateVm
    {
        /// <summary>Create New Virtual Machine (VM)</summary>
        public const string DialogTitle = "create_vm.dialog_title";
        /// <summary>VM Name (optional)</summary>
        public const string VmNameOptional = "create_vm.vm_name_optional";
        /// <summary>Game</summary>
        public const string GameLabel = "create_vm.game_label";
        /// <summary>Disk size is required.</summary>
        public const string DiskSizeRequired = "create_vm.disk_size_required";
        /// <summary>Game Server Configuration</summary>
        public const string GameServerConfigSection = "create_vm.game_server_config_section";
        /// <summary>This game does not require any additional parameters.</summary>
        public const string NoAdditionalParams = "create_vm.no_additional_params";
        /// <summary>CPU, RAM and Disk are required (CPU &gt;= 1, RAM &gt;= 256 MB, Disk &gt;= 1 GB).</summary>
        public const string ResourcesValidationError = "create_vm.resources_validation_error";
        /// <summary>VM creation has been queued.</summary>
        public const string VmCreationQueued = "create_vm.vm_creation_queued";
        /// <summary>Selected game requires {0} public ports. Installation may take several minutes.</summary>
        public const string PortsNotice = "create_vm.ports_notice";
    }

    public static class GameConfig
    {
        /// <summary>Game Configuration #{0}</summary>
        public const string DialogTitle = "game_config.dialog_title";
        /// <summary>Source:</summary>
        public const string SourceLabel = "game_config.source_label";
        /// <summary>This game has no dynamic configuration parameters.</summary>
        public const string NoDynamicParams = "game_config.no_dynamic_params";
        /// <summary>Refresh from server</summary>
        public const string RefreshFromServer = "game_config.refresh_from_server";
        /// <summary>Save &amp; Restart Game</summary>
        public const string SaveAndRestart = "game_config.save_and_restart";
        /// <summary>Configuration refreshed from server files.</summary>
        public const string RefreshedFromServer = "game_config.refreshed_from_server";
    }

    public static class Jobs
    {
        /// <summary>Action History for VM #{0}</summary>
        public const string DialogTitle = "jobs.dialog_title";
        /// <summary>Operation</summary>
        public const string OperationColumn = "jobs.operation_column";
        /// <summary>Date</summary>
        public const string DateColumn = "jobs.date_column";
    }

    public static class ResetPassword
    {
        /// <summary>Reset SSH Password</summary>
        public const string DialogTitle = "reset_password.dialog_title";
        /// <summary>Enter a new SSH password for the VM. Leave empty to generate a random password.</summary>
        public const string HelperText = "reset_password.helper_text";
        /// <summary>New password</summary>
        public const string NewPasswordLabel = "reset_password.new_password_label";
        /// <summary>Reset Password</summary>
        public const string SubmitButton = "reset_password.submit_button";
    }

    public static class AdminUsers
    {
        /// <summary>Users - Playmox Admin</summary>
        public const string PageTitle = "admin_users.page_title";
        /// <summary>Administrator permissions required.</summary>
        public const string AdminRequired = "admin_users.admin_required";
        /// <summary>User Management</summary>
        public const string Title = "admin_users.title";
        /// <summary>User List</summary>
        public const string UserListTab = "admin_users.user_list_tab";
        /// <summary>User</summary>
        public const string UserColumn = "admin_users.user_column";
        /// <summary>Limits (Quota)</summary>
        public const string LimitsColumn = "admin_users.limits_column";
        /// <summary>Ports</summary>
        public const string PortsLabel = "admin_users.ports_label";
        /// <summary>VM Statistics</summary>
        public const string VmStatisticsTab = "admin_users.vm_statistics_tab";
        /// <summary>Resources (in use)</summary>
        public const string ResourcesInUseColumn = "admin_users.resources_in_use_column";
        /// <summary>Disk:</summary>
        public const string DiskInlineLabel = "admin_users.disk_inline_label";
        /// <summary>Ports:</summary>
        public const string PortsInlineLabel = "admin_users.ports_inline_label";
        /// <summary>VM Statuses</summary>
        public const string VmStatusesColumn = "admin_users.vm_statuses_column";
        /// <summary>Add User</summary>
        public const string AddUserTitle = "admin_users.add_user_title";
        /// <summary>First Name</summary>
        public const string FirstNameLabel = "admin_users.first_name_label";
        /// <summary>Last Name</summary>
        public const string LastNameLabel = "admin_users.last_name_label";
        /// <summary>Initial Quota</summary>
        public const string InitialQuotaTitle = "admin_users.initial_quota_title";
        /// <summary>Create User</summary>
        public const string CreateUserButton = "admin_users.create_user_button";
        /// <summary>Error loading data: {0}</summary>
        public const string ErrorLoadingData = "admin_users.error_loading_data";
        /// <summary>Quota saved.</summary>
        public const string QuotaSaved = "admin_users.quota_saved";
        /// <summary>Delete User</summary>
        public const string DeleteUserTitle = "admin_users.delete_user_title";
        /// <summary>Are you sure you want to delete user {0}?</summary>
        public const string DeleteUserConfirm = "admin_users.delete_user_confirm";
        /// <summary>User deleted.</summary>
        public const string UserDeleted = "admin_users.user_deleted";
        /// <summary>User created.</summary>
        public const string UserCreated = "admin_users.user_created";
        /// <summary>VMs</summary>
        public const string MaxVms = "admin_users.max_vms";
        /// <summary>Run</summary>
        public const string ChipRunning = "admin_users.chip_running";
        /// <summary>Stop</summary>
        public const string ChipStopped = "admin_users.chip_stopped";
        /// <summary>Err</summary>
        public const string ChipError = "admin_users.chip_error";
    }

    public static class Config
    {
        /// <summary>Configuration - Playmox</summary>
        public const string PageTitle = "config.page_title";
        /// <summary>Panel Configuration</summary>
        public const string Title = "config.title";
        /// <summary>Proxmox Bootstrap Installation</summary>
        public const string BootstrapTitle = "config.bootstrap_title";
        /// <summary>Copy the command and run it on your Proxmox host as root (replace with your own API URL):</summary>
        public const string BootstrapInstructions = "config.bootstrap_instructions";
        /// <summary>Proxmox Connection</summary>
        public const string ConnectionTitle = "config.connection_title";
        /// <summary>Proxmox API URL</summary>
        public const string ProxmoxApiUrl = "config.proxmox_api_url";
        /// <summary>Template VMID</summary>
        public const string TemplateVmid = "config.template_vmid";
        /// <summary>Storage</summary>
        public const string Storage = "config.storage";
        /// <summary>Bridge</summary>
        public const string Bridge = "config.bridge";
        /// <summary>API user</summary>
        public const string ApiUser = "config.api_user";
        /// <summary>API token ID</summary>
        public const string ApiTokenId = "config.api_token_id";
        /// <summary>API token secret</summary>
        public const string ApiTokenSecret = "config.api_token_secret";
        /// <summary>Steam username</summary>
        public const string SteamUsername = "config.steam_username";
        /// <summary>Steam password</summary>
        public const string SteamPassword = "config.steam_password";
        /// <summary>Fill in the fields with the values from the bootstrap script output.</summary>
        public const string ConnectionSubtitle = "config.connection_subtitle";
        /// <summary>Playmox External Address (optional)</summary>
        public const string ExternalAddressLabel = "config.external_address_label";
        /// <summary>This address (e.g. playmox.mydomain.com) will replace the host IP in the client panel.</summary>
        public const string ExternalAddressHelper = "config.external_address_helper";
        /// <summary>Proxmox Node Name</summary>
        public const string NodeNameLabel = "config.node_name_label";
        /// <summary>Leave empty to keep unchanged</summary>
        public const string LeaveEmptyUnchanged = "config.leave_empty_unchanged";
        /// <summary>Currently set: YES</summary>
        public const string CurrentlySetYes = "config.currently_set_yes";
        /// <summary>Currently set: NO</summary>
        public const string CurrentlySetNo = "config.currently_set_no";
        /// <summary>Save Proxmox Configuration</summary>
        public const string SaveProxmoxButton = "config.save_proxmox_button";
        /// <summary>Platform Settings</summary>
        public const string PlatformSettingsTitle = "config.platform_settings_title";
        /// <summary>Anonymous login is supported. If you use an account with Steam Guard, installation may require approving multiple notifications.</summary>
        public const string SteamGuardAlert = "config.steam_guard_alert";
        /// <summary>Remove saved Steam password</summary>
        public const string RemoveSteamPassword = "config.remove_steam_password";
        /// <summary>Save Platform Settings</summary>
        public const string SavePlatformButton = "config.save_platform_button";
        /// <summary>Your Current Limits</summary>
        public const string CurrentLimitsTitle = "config.current_limits_title";
        /// <summary>CPU cores</summary>
        public const string CpuCoresLabel = "config.cpu_cores_label";
        /// <summary>Max instances</summary>
        public const string MaxInstancesLabel = "config.max_instances_label";
        /// <summary>Max public ports</summary>
        public const string MaxPublicPortsLabel = "config.max_public_ports_label";
        /// <summary>Failed to load quota limits.</summary>
        public const string FailedLoadQuota = "config.failed_load_quota";
        /// <summary>Failed to load platform settings.</summary>
        public const string FailedLoadPlatform = "config.failed_load_platform";
        /// <summary>Failed to load Proxmox configuration.</summary>
        public const string FailedLoadProxmox = "config.failed_load_proxmox";
        /// <summary>Failed to save Proxmox configuration.</summary>
        public const string FailedSaveProxmox = "config.failed_save_proxmox";
        /// <summary>Proxmox configuration saved.</summary>
        public const string ProxmoxSaved = "config.proxmox_saved";
        /// <summary>Failed to save platform settings.</summary>
        public const string FailedSavePlatform = "config.failed_save_platform";
        /// <summary>Platform settings saved.</summary>
        public const string PlatformSaved = "config.platform_saved";
        /// <summary>Command copied to clipboard.</summary>
        public const string CommandCopied = "config.command_copied";
        /// <summary>Failed to copy. Please copy the text manually.</summary>
        public const string FailedCopy = "config.failed_copy";
    }

    public static class Status
    {
        /// <summary>Creating</summary>
        public const string Creating = "status.creating";
        /// <summary>Running</summary>
        public const string Running = "status.running";
        /// <summary>Stopped</summary>
        public const string Stopped = "status.stopped";
        /// <summary>Updating</summary>
        public const string Updating = "status.updating";
        /// <summary>Deleting</summary>
        public const string Deleting = "status.deleting";
        /// <summary>Deleted</summary>
        public const string Deleted = "status.deleted";
        /// <summary>Queued</summary>
        public const string Queued = "status.queued";
        /// <summary>Succeeded</summary>
        public const string Succeeded = "status.succeeded";
        /// <summary>Failed</summary>
        public const string Failed = "status.failed";
        /// <summary>Cancelled</summary>
        public const string Cancelled = "status.cancelled";
    }

    public static class Operation
    {
        /// <summary>Start VM</summary>
        public const string StartVm = "operation.start_vm";
        /// <summary>Stop VM</summary>
        public const string StopVm = "operation.stop_vm";
        /// <summary>Shutdown VM</summary>
        public const string ShutdownVm = "operation.shutdown_vm";
        /// <summary>Restart VM</summary>
        public const string RestartVm = "operation.restart_vm";
        /// <summary>Update resources</summary>
        public const string UpdateResources = "operation.update_resources";
        /// <summary>Update game config</summary>
        public const string UpdateGameConfig = "operation.update_game_config";
    }

    public static class Game
    {
        /// <summary>Blank VM (No Game)</summary>
        public const string BlankVm = "game.blank_vm";
    }

    public static class Runtime
    {
        /// <summary>Blank virtual machine (no game server installed).</summary>
        public const string BlankVmMessage = "runtime.blank_vm_message";
        /// <summary>Game server is running.</summary>
        public const string ServerRunning = "runtime.server_running";
        /// <summary>Game server is stopped.</summary>
        public const string ServerStopped = "runtime.server_stopped";
        /// <summary>Virtual machine is not running.</summary>
        public const string VmNotRunning = "runtime.vm_not_running";
    }

}
