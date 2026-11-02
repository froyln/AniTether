import type { ListStatus } from './api'

export const STATUSES: ListStatus[] = ['Watching', 'Completed', 'PlanToWatch', 'OnHold', 'Dropped', 'Rewatching']

export const STATUS_LABELS: Record<ListStatus, string> = {
  Watching: 'Watching',
  Completed: 'Completed',
  PlanToWatch: 'Plan to watch',
  OnHold: 'On hold',
  Dropped: 'Dropped',
  Rewatching: 'Rewatching',
}
