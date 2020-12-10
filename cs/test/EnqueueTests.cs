﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FASTER.core;
using NUnit.Framework;


namespace FASTER.test
{

    [TestFixture]
    internal class EnqueueTests
    {
        private FasterLog log;
        private IDevice device;
        private string path = Path.GetTempPath() + "EnqueTests/";
        static readonly byte[] entry = new byte[20];
        static readonly ReadOnlySpanBatch spanBatch = new ReadOnlySpanBatch(20);

        public enum EnqueueIteratorType
        {
            Byte,
            SpanBatch,
            SpanByte
        }

        private struct ReadOnlySpanBatch : IReadOnlySpanBatch
        {
            private readonly int batchSize;
            public ReadOnlySpanBatch(int batchSize) => this.batchSize = batchSize;
            public ReadOnlySpan<byte> Get(int index) => entry; 
            public int TotalEntries() => batchSize;
        }

        [SetUp]
        public void Setup()
        {
            // Clean up log files from previous test runs in case they weren't cleaned up
            try {  new DirectoryInfo(path).Delete(true);  }
            catch {} 

            // Create devices \ log for test
            device = Devices.CreateLogDevice(path + "Enqueue", deleteOnClose: true);
            log = new FasterLog(new FasterLogSettings { LogDevice = device });
        }

        [TearDown]
        public void TearDown()
        {
            log.Dispose();

            // Clean up log files
            try { new DirectoryInfo(path).Delete(true); }
            catch { }
        }


        [Test]
        public void EnqueueBasicTest([Values] EnqueueIteratorType iteratorType)
        {
            int entryLength = 20;
            int numEntries = 1000; 
            int entryFlag = 9999;
            ReadOnlySpanBatch spanBatch = new ReadOnlySpanBatch(numEntries);

            // Set Default entry data
            for (int i = 0; i < entryLength; i++)
            {
                entry[i] = (byte)i;
            }

            // Enqueue but set each Entry in a way that can differentiate between entries
            for (int i = 0; i < numEntries; i++)
            {
                // Flag one part of entry data that corresponds to index
                if (i < entryLength)
                    entry[i] = (byte)entryFlag;

                // puts back the previous entry value
                if ((i > 0) && (i < entryLength))
                    entry[i - 1] = (byte)(i - 1);

                // Add to FasterLog
                switch (iteratorType)
                {
                    case EnqueueIteratorType.Byte:
                        // Default is add bytes so no need to do anything with it
                        log.Enqueue(entry);
                        break;
                    case EnqueueIteratorType.SpanByte:
                        // Could slice the span but for basic test just pass span of full entry - easier verification
                        Span<byte> spanEntry = entry;
                        log.Enqueue(spanEntry);
                        break;
                    case EnqueueIteratorType.SpanBatch:
                        log.Enqueue(spanBatch);
                        break;
                    default:
                        Assert.Fail("Unknown EnqueueIteratorType");
                        break;
                }
            }

            // Commit to the log
            log.Commit(false);

            // Read the log - Look for the flag so know each entry is unique
            int currentEntry = 0;
            using (var iter = log.Scan(0, 100_000_000))  
            {
                while (iter.GetNext(out byte[] result, out _, out _))
                {
                    if (currentEntry < entryLength)
                    {
                        // Span Batch only added first entry several times
                        if (iteratorType == EnqueueIteratorType.SpanBatch)
                        {
                            Assert.IsTrue(result[0] == (byte)entryFlag);  
                        }
                        else
                        {
                            Assert.IsTrue(result[currentEntry] == (byte)entryFlag); 
                        }

                        currentEntry++;
                    }
                }
            }

        }
    }
}

