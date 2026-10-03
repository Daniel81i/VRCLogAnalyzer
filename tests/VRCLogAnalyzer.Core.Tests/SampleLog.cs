namespace VRCLogAnalyzer.Core.Tests
{
    /// <summary>2026 年 10 月時点の VRChat ログ形式を模したサンプル（ID はすべて架空）</summary>
    internal static class SampleLog
    {
        public const string SelfId = "usr_00000000-0000-0000-0000-000000000001";
        public const string FriendId = "usr_00000000-0000-0000-0000-000000000002";
        public const string World1 = "wrld_11111111-1111-1111-1111-111111111111";
        public const string World2 = "wrld_22222222-2222-2222-2222-222222222222";

        public static readonly string Text = string.Join("\n", new[]
        {
            "2026.10.02 21:14:06 Debug      -  [API] Requesting Get config {{}} retryCount: 2",
            $"2026.10.02 21:14:07 Debug      -  User Authenticated: Me (\"Self\") ({SelfId})",
            $"2026.10.02 21:14:09 Debug      -  [Behaviour] Destination set: {World1}:47371~private({SelfId})~region(jp)",
            "2026.10.02 21:14:10 Debug      -  [Behaviour] Entering Room: ［JP］ Test World ワールド",
            $"2026.10.02 21:14:10 Debug      -  [Behaviour] Joining {World1}:47371~private({SelfId})~region(jp)",
            "2026.10.02 21:14:10 Debug      -  [Behaviour] Joining or Creating Room: ［JP］ Test World ワールド",
            "",
            "2026.10.02 21:14:23 Debug      -  [Video Playback] Attempting to resolve URL 'https://example.com/live?p=o&w=test'",
            "2026.10.02 21:14:23 Debug      -  [Behaviour] OnPlayerJoinComplete Me (\"Self\")",
            $"2026.10.02 21:14:24 Debug      -  [Behaviour] OnPlayerJoined Me (\"Self\") ({SelfId})",
            "2026.10.02 21:14:24 Debug      -  [Behaviour] Initialized PlayerAPI \"Me (\"Self\")\" is local",
            $"2026.10.02 21:14:30 Debug      -  [Behaviour] OnPlayerJoined Friend (Away) ({FriendId})",
            "2026.10.02 21:14:34 Error      -  [UdonBehaviour] An exception occurred during Udon execution, this UdonBehaviour will be halted.",
            "VRC.Udon.VM.UdonVMException: An exception occurred in an UdonVM, execution will be halted.",
            "  at VRC.Udon.VM.UdonVM.Interpret () [0x00000] in <00000000000000000000000000000000>:0 ",
            "",
            "2026.10.02 21:14:40 Warning    -  Some warning",
            "  warning stack line that must not be attached",
            $"2026.10.02 21:15:20 Debug      -  [Behaviour] OnPlayerLeft Friend (Away) ({FriendId})",
            "2026.10.02 21:15:26 Debug      -  [Behaviour] OnPlayerLeftRoom",
            "2026.10.02 21:15:27 Debug      -  [Behaviour] OnLeftRoom",
            $"2026.10.02 21:15:27 Debug      -  [Behaviour] OnPlayerLeft Me (\"Self\") ({SelfId})",
            "2026.10.02 21:15:28 Debug      -  [Behaviour] Entering Room: Second_World 100%",
            $"2026.10.02 21:15:28 Debug      -  [Behaviour] Joining {World2}:92747~region(jp)",
            "2026.10.02 21:15:28 Debug      -  [Behaviour] Joining or Creating Room: Second_World 100%",
            "2026.10.02 21:15:40 Error      -  Last line error without trace",
        });
    }
}
