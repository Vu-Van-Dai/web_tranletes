import { Badge } from '@/components/ui';
import { chapterStatusLabel, jobStageLabel, jobStatusLabel } from '@/text/vi';
import type { ChapterStatus, JobStatus, JobSummary, ProcessingJobItem } from '@/types/api';
import { ProgressBar } from '@/components/ui';

const chapterTone: Record<ChapterStatus, 'neutral' | 'accent' | 'success' | 'warn' | 'danger'> = {
  draft: 'neutral',
  processing: 'warn',
  ready: 'accent',
  published: 'success',
  unpublished: 'neutral',
  failed: 'danger',
};

export function ChapterStatusBadge({ status }: { status: ChapterStatus }) {
  return <Badge tone={chapterTone[status]} data-testid="chapter-status">{chapterStatusLabel[status]}</Badge>;
}

const jobTone: Record<JobStatus, 'neutral' | 'warn' | 'success' | 'danger'> = {
  queued: 'neutral',
  running: 'warn',
  succeeded: 'success',
  failed: 'danger',
  canceled: 'neutral',
};

export function JobStatusBadge({ status }: { status: JobStatus }) {
  return <Badge tone={jobTone[status]}>{jobStatusLabel[status]}</Badge>;
}

export const isActiveJob = (job: Pick<JobSummary | ProcessingJobItem, 'status'> | null | undefined) => job?.status === 'queued' || job?.status === 'running';

export function JobProgress({ job }: { job: Pick<JobSummary, 'status' | 'stage' | 'progressDone' | 'progressTotal'> }) {
  const fraction = job.progressTotal > 0 ? job.progressDone / job.progressTotal : job.status === 'succeeded' ? 1 : 0;
  const label = job.stage === 'converting' && job.progressTotal > 0
    ? `${jobStageLabel[job.stage]} (${job.progressDone}/${job.progressTotal} trang)`
    : jobStageLabel[job.stage];
  return <ProgressBar value={fraction} label={label} tone={job.status === 'failed' ? 'danger' : job.status === 'succeeded' ? 'success' : 'accent'} />;
}
