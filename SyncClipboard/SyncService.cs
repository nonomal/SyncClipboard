﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SyncClipboard
{
    class SyncService
    {
        private MainForm mainForm;
        private Thread pullThread;
        private Thread pushThread;
        private String oldString = "";

        public bool isStop = false;


        private bool isStatusError = false;
        private bool isTimeOut = false;
        private bool isFirstTime = true;
        private bool isPushError = false;
        private String pushErrorMessage;

        private int errorTimes = 0;
        private int pullTimeoutTimes = 0;


        public SyncService(MainForm mf)
        {
            this.mainForm = mf;
        }
        public void Start()
        {
            pullThread = new Thread(PullLoop);
            pullThread.SetApartmentState(ApartmentState.STA);
            pullThread.Start();
        }
        public void StartPush()
        {
            if (pushThread != null)
                pushThread.Abort();
            pushThread = new Thread(PushLoop);
            pushThread.SetApartmentState(ApartmentState.STA);
            pushThread.Start();
        }

        public void Stop()
        {
            isStop = true;
            try
            {
                pullThread.Abort();
                pushThread.Abort();
            }
            catch { }
        }

        public void OpenURL()
        {
            if(this.oldString.Length < 4)
                return;
            if(this.oldString.Substring(0,4) == "http")
                System.Diagnostics.Process.Start(this.oldString);  
        }

        private void PullLoop()
        {
            while (!this.isStop)
            {
                if (Config.IfPull)
                    this.PullFromRemote();
                Thread.Sleep((int)Config.IntervalTime);
            }
        }
        private void PullFromRemote()
        {
            String url = Config.Url;
            String auth = "Authorization: Basic " + Config.Auth;

            Console.WriteLine (auth +"dd");
            HttpWebResponse httpWebResponse = GetPullResponse(url, auth);
            if (this.isStatusError || this.isTimeOut)
            {
                try { httpWebResponse.Close(); }
                catch { }
                return;
            }
            errorTimes = this.pullTimeoutTimes = 0;
            StreamReader objStrmReader = new StreamReader(httpWebResponse.GetResponseStream());
            String strReply = objStrmReader.ReadToEnd();
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            ConvertJsonClass p1=null;
            try
            {
                p1 = serializer.Deserialize<ConvertJsonClass>(strReply);
            }
            catch
            {
                return;
            }
            if (p1 != null && p1.Clipboard != this.oldString)
            {
                if (this.isFirstTime)
                {
                    this.isFirstTime = false;
                    this.oldString = p1.Clipboard;
                    return;
                }
                Clipboard.SetData(DataFormats.Text, p1.Clipboard);
                this.oldString = p1.Clipboard;
                this.mainForm.setLog(true, false, "剪切板同步成功", this.SafeMessage(oldString), null, "info");
            }
            else
            {
                this.mainForm.setLog(false, true, "服务器连接成功", null, "正在同步", "info");
            }
            try { httpWebResponse.Close(); }
            catch { }
        }
        private HttpWebResponse GetPullResponse(String url, String auth)
        {
            HttpWebResponse httpWebResponse = null;
            try
            {
                httpWebResponse = HttpWebResponseUtility.CreateGetHttpResponse(url, Config.TimeOut, null, auth, null);
                if (this.isStatusError || this.isTimeOut)
                {
                    this.mainForm.setLog(true, true, "连接服务器成功", "正在同步", "正在同步", "info");
                    this.isTimeOut = false;
                }
                if (httpWebResponse.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    this.errorTimes += 1;
                    if (this.errorTimes == Config.RetryTimes + 1)
                    {
                        this.mainForm.setLog(true, true, "服务器状态错误：" + httpWebResponse.StatusCode.ToString(), "重试次数:" + errorTimes.ToString(), "重试次数:" + errorTimes.ToString(), "erro");
                    }
                    else
                    {
                        this.mainForm.setLog(false, true, "服务器状态错误：" + httpWebResponse.StatusCode.ToString(), "重试次数:" + errorTimes.ToString(), "重试次数:" + errorTimes.ToString(), "erro");
                    }
                    isStatusError = true;
                }
                isStatusError = false;
            }
            catch (Exception ex)
            {
                this.pullTimeoutTimes += 1;
                isTimeOut = true;
                if (this.pullTimeoutTimes == Config.RetryTimes + 1)
                {
                    Console.WriteLine(ex.ToString());
                    this.mainForm.setLog(true, true, ex.Message.ToString(), url + "\n重试次数:" + this.pullTimeoutTimes.ToString(), "重试次数:" + this.pullTimeoutTimes.ToString(), "erro");
                }
                else
                {
                    Console.WriteLine(ex.ToString());
                    this.mainForm.setLog(false, true, ex.Message.ToString(), url + "\n重试次数:" + this.pullTimeoutTimes.ToString(), "重试次数:" + this.pullTimeoutTimes.ToString(), "erro");
                }
            }
            return httpWebResponse;
        }
        public void PushLoop()
        {
            IDataObject iData = Clipboard.GetDataObject();
            if (!iData.GetDataPresent(DataFormats.Text))
            {
                return;
            }
            string str = (String)iData.GetData(DataFormats.Text);
            for (int i = 0; i < Config.RetryTimes; i++)
            {
                if(this.isStop || (!Config.IfPush))
                {
                    return;
                }
                this.PushToRemote(str);
                if (this.isPushError)
                {
                    continue;
                }
                return;
            }
            this.mainForm.setLog(true, false, this.pushErrorMessage, "未同步：" + this.SafeMessage(str), null, "erro");
        }
        public void PushToRemote(String str)
        {
            ConvertJsonClass convertJson = new ConvertJsonClass();
            convertJson.File = "";
            convertJson.Clipboard = str;
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string jsonString = serializer.Serialize(convertJson);

            String url = Config.Url;
            String auth = "Authorization: Basic " + Config.Auth;
            HttpWebResponse httpWebResponse = null;
            try
            {
                httpWebResponse = HttpWebResponseUtility.CreatePutHttpResponse(url, jsonString, Config.TimeOut, null, auth, null, null);
            }
            catch(Exception ex)
            {
                this.isPushError  = true;
                this.pushErrorMessage = ex.Message.ToString();
                return;
            }
            if (httpWebResponse.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                //this.notifyIcon1.ShowBalloonTip(5, "剪切板同步到云", httpWebResponse.StatusCode.GetHashCode().ToString(), ToolTipIcon.None);
                this.isPushError = false;
                this.oldString = str;
            }
            else
            {
                pushErrorMessage = httpWebResponse.StatusCode.GetHashCode().ToString();
                this.isPushError = true;
            }
        }
        private String SafeMessage(String str)
        {
            string msgString;
            try
            {
                msgString = str.Substring(0, 40) + "...";
            }
            catch
            {
                msgString = str;
            }
            return msgString;
        }
    }
}
