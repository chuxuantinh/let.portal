﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LetPortal.Core.Persistences;
using LetPortal.Core.Utils;
using LetPortal.Microservices.Server.Entities;
using LetPortal.Microservices.Server.Repositories.Abstractions;

namespace LetPortal.Microservices.Server.Repositories.Implements
{
    public class MonitorHttpReportEFRepository : EFGenericRepository<MonitorHttpReport>, IMonitorHttpReportRepository
    {
        private readonly LetPortalServiceManagementDbContext _context;
        public MonitorHttpReportEFRepository(LetPortalServiceManagementDbContext context)
            : base(context)
        {
            _context = context;
        }

        public Task CollectDataAsync(string[] collectServiceIds, DateTime reportDate, int duration, bool roundDate = true)
        {
            duration /= 60;
            var allInsertCounters = new List<MonitorHttpReport>();
            var nearestMinute = 0;
            var allRequiredCounters = new List<HttpCounter>();
            var allowMinutes = new List<int>
            {
              0
            };

            for (var i = duration; i < 60; i += duration)
            {
                if (i < reportDate.Minute)
                {
                    nearestMinute = i;
                }
                allowMinutes.Add(i);
            }
            var endDate = new DateTime(
                                        reportDate.Year,
                                        reportDate.Month,
                                        reportDate.Day,
                                        reportDate.Hour,
                                        nearestMinute,
                                        1,
                                        DateTimeKind.Utc);

            var startDate = endDate.AddMinutes(duration * -1);
            var lastestRecord = _context.MonitorHttpReports.Where(a => collectServiceIds.Contains(a.ServiceId)).OrderByDescending(a => a.ReportedDate).FirstOrDefault();
            if (lastestRecord != null)
            {
                var lastMinute = lastestRecord.ReportedDate.Minute;
                var nextStartDate = lastestRecord.ReportedDate.AddMinutes(duration);
                // Ensure report must be over last record
                if (nextStartDate < reportDate)
                {
                    startDate = new DateTime(
                                        nextStartDate.Year,
                                        nextStartDate.Month,
                                        nextStartDate.Day,
                                        nextStartDate.Hour,
                                        nextStartDate.Minute,
                                        0,
                                        DateTimeKind.Utc);

                    allRequiredCounters = _context.HttpCounters.Where(a => collectServiceIds.Contains(a.ServiceId) && a.MeansureDate >= startDate && a.MeansureDate < endDate).OrderBy(b => b.MeansureDate).ToList();
                }
            }
            else
            {
                startDate = new DateTime(
                                        reportDate.Year,
                                        reportDate.Month,
                                        reportDate.Day,
                                        reportDate.Hour,
                                        0,
                                        0,
                                        DateTimeKind.Utc);

                allRequiredCounters = _context.HttpCounters.Where(a => collectServiceIds.Contains(a.ServiceId) && a.MeansureDate >= startDate && a.MeansureDate < endDate).OrderBy(b => b.MeansureDate).ToList();
            }



            if (allRequiredCounters.Any())
            {
                var counter = allRequiredCounters.Count;
                var startMinute = allRequiredCounters.First().MeansureDate.Minute;

                for (var i = 0; i < allowMinutes.Count; i++)
                {
                    if (allowMinutes[i] <= startMinute && allowMinutes[i + 1] > startMinute)
                    {
                        startMinute = allowMinutes[i];
                        break;
                    }
                }

                var firstCounterDate = allRequiredCounters.First().MeansureDate;
                var startCompareDate = new DateTime(
                                        firstCounterDate.Year,
                                        firstCounterDate.Month,
                                        firstCounterDate.Day,
                                        firstCounterDate.Hour,
                                        startMinute,
                                        0,
                                        DateTimeKind.Utc);
                var endCompareDate = startCompareDate.AddMinutes(duration);
                var services = allRequiredCounters.GroupBy(a => a.ServiceId);

                while (counter > 0)
                {
                    foreach (var service in services)
                    {
                        var proceedingCounters = service.Where(a => a.MeansureDate >= startCompareDate && a.MeansureDate < endCompareDate);

                        if (proceedingCounters.Any())
                        {
                            counter -= proceedingCounters.Count();
                            var firstCounter = proceedingCounters.First();
                            var lastCounter = proceedingCounters.Last();
                            var successRequests = firstCounter.Id != lastCounter.Id ? lastCounter.SuccessRequests - firstCounter.SuccessRequests : firstCounter.SuccessRequests;
                            var failedRequests = firstCounter.Id != lastCounter.Id ? lastCounter.FailedRequests - firstCounter.FailedRequests : firstCounter.FailedRequests;
                            var avgDuration =
                                firstCounter.Id != lastCounter.Id && (successRequests + failedRequests) > 0 ?
                                proceedingCounters.Average(a => a.AvgDuration) : 0;
                            var newReportCounter = new MonitorHttpReport
                            {
                                Id = DataUtil.GenerateUniqueId(),
                                SuccessRequests = successRequests,
                                FailRequests = failedRequests,
                                AvgDuration = avgDuration,
                                ReportedDate = startCompareDate,
                                ServiceId = service.Key
                            };

                            newReportCounter.TotalRequests = newReportCounter.SuccessRequests + newReportCounter.FailRequests;
                            allInsertCounters.Add(newReportCounter);
                        }
                        else
                        {
                            var newReportCounter = new MonitorHttpReport
                            {
                                Id = DataUtil.GenerateUniqueId(),
                                SuccessRequests = 0,
                                FailRequests = 0,
                                AvgDuration = 0,
                                ReportedDate = startCompareDate,
                                ServiceId = service.Key
                            };
                            allInsertCounters.Add(newReportCounter);
                        }
                    }

                    startCompareDate = startCompareDate.AddMinutes(duration);
                    endCompareDate = startCompareDate.AddMinutes(duration);
                }

                _context.MonitorHttpReports.AddRange(allInsertCounters.ToArray());
                _context.SaveChanges();
            }
            return Task.CompletedTask;
        }
    }
}
