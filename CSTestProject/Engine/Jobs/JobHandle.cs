using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine.Jobs {
    public readonly struct JobHandle {

        private readonly int packed;

        public readonly int Id => packed & 0x7fff;
        public readonly byte Version => (byte)(packed >> 24);
        public readonly bool IsValid => packed != 0;

        public JobHandle(int id, byte version) { packed = (id | 0x10000) | (version << 24); }
        public override string ToString() { return $"<{Id} V={Version}>"; }

        public static JobHandle CombineDependencies(JobHandle job1) { return job1; }
        public static JobHandle CombineDependencies(JobHandle job1, JobHandle job2) {
            if (!job1.IsValid) return job2;
            if (!job2.IsValid) return job1;
            var dependencies = JobDependencies.Instance;
            bool j1Complete = dependencies.GetIsComplete(job1);
            bool j2Complete = dependencies.GetIsComplete(job2);
            if (j1Complete && j2Complete) return JobHandle.None;
            return dependencies.JoinHandles(job1, job2);
        }

        public static JobHandle Schedule(Action<object> value, JobHandle dependency = default) {
            return JobDependencies.Instance.CreateHandle(JobScheduler.Instance.CreateJob(value), dependency);
        }
        public static JobHandle Schedule(Action<object> value, object context, JobHandle dependency) {
            return JobDependencies.Instance.CreateHandle(JobScheduler.Instance.CreateJob(value, context), dependency);
        }

        public static readonly JobHandle None = new();
    }
}
