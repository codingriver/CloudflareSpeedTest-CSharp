#if !UNITY_BUILD
// CLI 入口 - 业务逻辑已迁移到 CfstRunner.cs
// Unity 等宿主请直接使用 CfstRunner.RunSpeedTestAsync(config, ct)
var exitCode = await CloudflareST.CfstRunner.RunCliAsync(args);
Environment.Exit(exitCode);
#endif
