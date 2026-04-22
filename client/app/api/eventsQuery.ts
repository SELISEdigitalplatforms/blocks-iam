export const MIN_SEARCH_LEN = 3;

export type EventsListParams = {
  page: number;
  pageSize: number;
  sortBy: 'name' | 'start' | 'end';
  sortDir: 'asc' | 'desc';
  /** Sent to API only when length >= MIN_SEARCH_LEN */
  search: string;
  rangeStart: string;
  rangeEnd: string;
};

export function buildEventsListUrl(params: EventsListParams): string {
  const q = new URLSearchParams();
  q.set('page', String(params.page));
  q.set('pageSize', String(params.pageSize));
  q.set('sortBy', params.sortBy);
  q.set('sortDir', params.sortDir);

  const s = params.search.trim();
  if (s.length >= MIN_SEARCH_LEN) q.set('search', s);

  if (params.rangeStart) q.set('rangeStart', new Date(params.rangeStart).toISOString());
  if (params.rangeEnd) q.set('rangeEnd', new Date(params.rangeEnd).toISOString());

  return `/api/events?${q.toString()}`;
}
