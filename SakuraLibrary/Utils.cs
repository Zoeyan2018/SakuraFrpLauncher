﻿using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.ServiceProcess;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace SakuraLibrary
{
    public static class Utils
    {
        public static readonly DateTime SakuraTimeBase = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static readonly string LibraryPath = Assembly.GetExecutingAssembly().Location;
        public static readonly string ExecutablePath = Process.GetCurrentProcess().MainModule.FileName;

        public static readonly bool IsAdministrator = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        public static readonly string InstallationHash = Md5(Assembly.GetExecutingAssembly().Location);
        public static readonly string InstallationPipeName = InstallationHash + "_" + Consts.PipeName;

        public static string Md5(byte[] data)
        {
            try
            {
                StringBuilder Result = new StringBuilder();
                foreach (byte Temp in new MD5CryptoServiceProvider().ComputeHash(data))
                {
                    if (Temp < 16)
                    {
                        Result.Append("0");
                        Result.Append(Temp.ToString("x"));
                    }
                    else
                    {
                        Result.Append(Temp.ToString("x"));
                    }
                }
                return Result.ToString();
            }
            catch
            {
                return "0000000000000000";
            }
        }

        public static string Md5(string Data) => Md5(Encoding.UTF8.GetBytes(Data));

        public static int CastInt(dynamic item)
        {
            switch (item)
            {
            case int i:
                return i;
            case long l:
                return (int)l;
            case string s:
                return int.Parse(s);
            default:
                return (int)item;
            }
        }

        public static string GetAutoRunFile(string prefix) => Environment.GetFolderPath(Environment.SpecialFolder.Startup) + "\\" + prefix + Md5(ExecutablePath) + ".lnk";

        public static string SetAutoRun(bool start, string prefix)
        {
            try
            {
                var file = GetAutoRunFile(prefix);
                if (start)
                {
                    if (File.Exists(file))
                    {
                        return null;
                    }
                    var shortcut = (IWshRuntimeLibrary.IWshShortcut)new IWshRuntimeLibrary.WshShell().CreateShortcut(file);
                    shortcut.TargetPath = ExecutablePath;
                    shortcut.Arguments = "--minimize";
                    shortcut.Description = "SakuraFrp Launcher 开机自启";
                    shortcut.Save();
                }
                else if (File.Exists(file))
                {
                    File.Delete(file);
                }
                return null;
            }
            catch (Exception e)
            {
                return "无法设置开机启动, 请检查杀毒软件是否拦截了此操作\n\n" + e.ToString();
            }
        }

        public static string SetServicePermission()
        {
            var sc = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == Consts.ServiceName);
            if (sc == null)
            {
                return "Service not found";
            }

            var buffer = new byte[0];
            if (!NTAPI.QueryServiceObjectSecurity(sc.ServiceHandle, SecurityInfos.DiscretionaryAcl, buffer, 0, out uint size))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != 122 && err != 0) // ERROR_INSUFFICIENT_BUFFER
                {
                    return "QueryServiceObjectSecurity[1] error: " + err;
                }
                buffer = new byte[size];
                if (!NTAPI.QueryServiceObjectSecurity(sc.ServiceHandle, SecurityInfos.DiscretionaryAcl, buffer, size, out size))
                {
                    return "QueryServiceObjectSecurity[2] error: " + Marshal.GetLastWin32Error();
                }
            }

            var rsd = new RawSecurityDescriptor(buffer, 0);

            var dacl = new DiscretionaryAcl(false, false, rsd.DiscretionaryAcl);
            dacl.SetAccess(AccessControlType.Allow, new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), (int)(ServiceAccessRights.SERVICE_QUERY_STATUS | ServiceAccessRights.SERVICE_START | ServiceAccessRights.SERVICE_STOP | ServiceAccessRights.SERVICE_INTERROGATE), InheritanceFlags.None, PropagationFlags.None);

            buffer = new byte[dacl.BinaryLength];
            dacl.GetBinaryForm(buffer, 0);

            rsd.DiscretionaryAcl = new RawAcl(buffer, 0);

            buffer = new byte[rsd.BinaryLength];
            rsd.GetBinaryForm(buffer, 0);

            if (!NTAPI.SetServiceObjectSecurity(sc.ServiceHandle, SecurityInfos.DiscretionaryAcl, buffer))
            {
                return "SetServiceObjectSecurity error: " + Marshal.GetLastWin32Error();
            }
            return null;
        }

        public static Process[] SearchProcess(string name, string testPath = null)
        {
            return Process.GetProcessesByName(name).Where(p =>
            {
                try
                {
                    uint size = 256;
                    var sb = new StringBuilder((int)size - 1);
                    if (NTAPI.QueryFullProcessImageName(p.Handle, 0, sb, ref size))
                    {
                        return testPath == null || Path.GetFullPath(sb.ToString()) == testPath;
                    }
                }
                catch { }
                return false;
            }).ToArray();
        }

        public static uint GetSakuraTime() => (uint)DateTime.UtcNow.Subtract(SakuraTimeBase).TotalSeconds;

        public static DateTime ParseSakuraTime(uint seconds) => SakuraTimeBase.AddSeconds(seconds).ToLocalTime();

        public static void VerifySignature(params string[] files)
        {
#if !DEBUG // && false
            try
            {
                var key = new PublicKey(new Oid("1.2.840.113549.1.1.1"), new AsnEncodedData(new byte[] { 05, 00 }), new AsnEncodedData(Properties.Resources.sakura_sign));
                using (var rsa = (RSACryptoServiceProvider)key.Key)
                {
                    var failure = files.Where(f => !File.Exists(f + ".sig") || !rsa.VerifyData(File.ReadAllBytes(f), "SHA256", File.ReadAllBytes(f + ".sig"))).ToList();
                    if (failure.Count == 0)
                    {
                        return;
                    }
                    NTAPI.MessageBox(0, "@@@@@@@@@@@@@@@@@@\n" +
                        "         !!!  警告: 文件签名验证失败  !!!\n" +
                        "@@@@@@@@@@@@@@@@@@\n\n" +
                        "下列文件未通过数字签名校验:\n" + string.Join("\n", failure) + "\n\n" +
                        "这些文件可能已损坏或被纂改, 这意味着您的电脑可能已经被病毒感染, 请立即进行杀毒并重新下载启动器\n\n" +
                        "如果您准备自己编译启动器或使用其他版本的 frpc, 请自行修改 SakuraLibrary\\Utils.cs 或使用 Debug 构建来禁用签名验证", "Error", 0x10);
                }
            }
            catch (Exception e)
            {
                NTAPI.MessageBox(0, "@@@@@@@@@@@@@@@@@@\n" +
                    "         !!!  警告: 文件签名验证失败  !!!\n" +
                    "@@@@@@@@@@@@@@@@@@\n" +
                    "出现内部错误, 请截图此报错并联系管理员\n\n" + e, "Error", 0x10);
            }
            Environment.Exit(0);
#endif
        }
    }
}
