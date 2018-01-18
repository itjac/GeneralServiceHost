﻿using FluentScheduler;
using GeneralServiceHost.Common;
using GeneralServiceHost.Helper;
using GeneralServiceHost.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace GeneralServiceHost.Manager
{
    public class JobInfoManager
    {

        static JobInfoManager()
        {
            JobManager.JobEnd += JobManager_JobEnd;
            JobManager.JobStart += JobManager_JobStart;
        }

        private static void JobManager_JobStart(JobStartInfo obj)
        {
            Refresh();
        }

        private static void JobManager_JobEnd(JobEndInfo obj)
        {
            Refresh();
        }

        /// <summary>
        /// 刷新状态
        /// </summary>
        public static void Refresh()
        {
            foreach (var jobItem in DataManager.Current.JobInfos)
            {

                var c = JobManager.AllSchedules.FirstOrDefault(d => d.Name == jobItem.Name);
                if (DataManager.Current.RunningJob.Contains(jobItem.Name))
                {
                    jobItem.Status = JobStatusType.Running;

                }
                else
                {
                    if (c != null)
                    {
                        jobItem.Name = c.Name;
                        jobItem.NextRun = c.NextRun;
                        if (c.Disabled)
                        {
                            jobItem.Status = JobStatusType.Stop;
                        }
                        else
                        {
                            jobItem.Status = JobStatusType.Pending;

                        }
                    }
                    else
                    {
                        jobItem.NextRun = DateTime.MinValue;
                        jobItem.Status = JobStatusType.Obsolete;

                    }

                }
            }

            DataManager.Current.Save();
        }

        /// <summary>
        /// 开始Job操作
        /// </summary>
        /// <param name="name"></param>
        public static void Start(string name)
        {
            var currentJobInfo = DataManager.Current.JobInfos.First(c => c.Name == name);
            if (currentJobInfo.Status == JobStatusType.Stop)
            {
                JobManager.GetSchedule(name).Enable();

            }
            else if (currentJobInfo.Status == JobStatusType.Obsolete)
            {
                DataManager.Current.JobInfos.Remove(currentJobInfo);

                var isSuccess = RunSchedule(currentJobInfo.ScheduleInfo);
                if (isSuccess)
                {
                    var isCreateJobSuccess = JobInfoManager.CreateJob(currentJobInfo.ScheduleInfo);
                    if (isCreateJobSuccess)
                    {
                        MessageBox.Show("任务启用成功");

                    }
                    else
                    {
                        MessageBox.Show("任务启用失败");

                    }
                    JobInfoManager.Refresh();
                }
                else
                {
                    MessageBox.Show("任务启用失败");
                }
            }

            else
            {
                MessageBox.Show("任务已经在启用状态");

            }
            Refresh();
        }

        /// <summary>
        /// 中断Job操作
        /// </summary>
        /// <param name="name"></param>
        public static void Abort(string name)
        {
            if (DataManager.Current.JobInfos.First(c => c.Name == name).Status == JobStatusType.Pending)
            {
                JobManager.GetSchedule(name).Disable();

            }
            Refresh();
        }

        /// <summary>
        /// 运行计划单
        /// </summary>
        /// <param name="ScheduleInfo"></param>
        public static bool RunSchedule(ScheduleInfo ScheduleInfo)
        {
            GeneralServiceRegistry _generalServiceRegistry = new GeneralServiceRegistry();
            if (DataManager.Current.JobInfos.Where(c => c.Name == ScheduleInfo.Name).Any())
            {
                MessageBox.Show(string.Format("已经包含名称为{0}的Job", ScheduleInfo.Name));

                return false;
            }

            if (ScheduleInfo.IsGeneralJob)
            {
                ProcessResult result;
                _generalServiceRegistry.SetAndRegistryGeneralService(ScheduleInfo, (sch) =>
                {
                    if (ScheduleInfo.IsGuard)
                    {
                        var processMgr = new ProcessManager(sch);
                        result = processMgr.RunProcess(OutputAction, ErrorAction);
                    }
                    else
                    {
                        var processMgr = new ProcessManager(sch);
                        result = processMgr.RunProcess(OutputAction);
                    }

                    FinishJob(result);
                });

            }
            else
            {
                _generalServiceRegistry.SetAndRegistryDelayService(ScheduleInfo, (sch) =>
                {
                    ProcessResult result;
                    if (ScheduleInfo.IsGuard)
                    {
                        var processMgr = new ProcessManager(sch);
                        result = processMgr.RunProcess(OutputAction, ErrorAction);
                    }
                    else
                    {
                        var processMgr = new ProcessManager(sch);
                        result = processMgr.RunProcess(OutputAction);
                    }

                    FinishJob(result);

                });

            }

            JobManager.Initialize(_generalServiceRegistry);
            return true;
        }

        /// <summary>
        /// 对Job做完成处理
        /// </summary>
        /// <param name="result"></param>
        private static void FinishJob(ProcessResult result)
        {
            var currentJob = DataManager.Current.JobInfos.FirstOrDefault(c => c.Name == result.Name);
            currentJob.LastRun = result.Starttime;

            switch (result.Status)
            {
                case -1:

                    currentJob.RunCount++;
                    currentJob.ErrorCount++;

                    break;
                case 1:
                    currentJob.RunCount++;
                    currentJob.SucessCount++;
                    break;
                case 0:
                    currentJob.ErrorCount++;
                    break;
                default:
                    break;
            }
            DataManager.Current.Save();
        }

        /// <summary>
        /// 添加任务
        /// </summary>
        /// <param name="ScheduleInfo"></param>
        public static bool CreateJob(ScheduleInfo ScheduleInfo)
        {
            var schedule = JobManager.GetSchedule(ScheduleInfo.Name);
            if (schedule != null)
            {

                var jobInfo = new JobInfo()
                {
                    Name = schedule.Name,
                    NextRun = schedule.NextRun,
                    ScheduleInfo = ScheduleInfo,
                    LastRun = DateTime.MinValue,
                    RunCount = 0
                };


                DataManager.Current.JobInfos.Add(jobInfo);
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// 运行程序异常
        /// </summary>
        /// <param name="sender"></param>
        private static void ErrorAction(object sender)
        {
            var sch = sender as ScheduleInfo;
            var processMgr = new ProcessManager(sch);
            var result = processMgr.RunProcess(OutputAction, ErrorAction);
            FinishJob(result);
        }

        /// <summary>
        /// 程序输出
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="arg2"></param>
        private static void OutputAction(object sender, OutputArgs arg2)
        {
            var name = arg2.JobName;
            var content = arg2.OutputContent;
            var createTime = DateTime.Now;
            var log = DataManager.Current.JobInfos.FirstOrDefault(c => c.Name == name)?.SbLog;
            var value = string.Format("[{0}]{1}", createTime.ToString("yyyy-MM-dd hh:mm:ss"), content);
            App.Current.Dispatcher.Invoke((Action)delegate
            {
                log.Add(value);
            });
            OutputManager.AppendOutput(name, value + "\n");

        }


    }
}
