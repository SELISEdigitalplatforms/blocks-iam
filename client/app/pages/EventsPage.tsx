import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { type FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import { buildEventsListUrl, MIN_SEARCH_LEN, type EventsListParams } from '../api/eventsQuery';
import { apiJson } from '../api/http';
import type { CreateEventBody, EventItem, PagedEvents, UpdateEventBody } from '../api/types';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import {
  eventLocalToIso,
  isoToDateAndTime,
  rangeEndToIso,
  rangeStartToIso,
} from '../utils/datetimeLocal';

const pageSizeOptions = [5, 10, 20, 50] as const;
const SEARCH_DEBOUNCE_MS = 400;

const inputClass =
  'min-h-11 w-full rounded-xl border border-white/10 bg-slate-950/80 px-4 py-2.5 text-sm text-white placeholder:text-slate-500 focus:border-teal-400 focus:outline-none focus:ring-2 focus:ring-teal-500/30 disabled:opacity-60';
const labelClass = 'block text-xs font-medium uppercase tracking-wide text-slate-400';

function FilterIcon({ className }: { className?: string }) {
  return (
    <svg
      className={className}
      width={20}
      height={20}
      viewBox="0 0 24 24"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      aria-hidden
    >
      <path
        d="M3 5h18l-7 8v6l-4 2v-8L3 5z"
        stroke="currentColor"
        strokeWidth={1.75}
        strokeLinejoin="round"
      />
    </svg>
  );
}

function formatRange(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return d.toLocaleString(undefined, {
    weekday: 'short',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

type FormState = {
  name: string;
  startDate: string;
  startTime: string;
  endDate: string;
  endTime: string;
  description: string;
  location: string;
  organizer: string;
};

const emptyForm = (): FormState => ({
  name: '',
  startDate: '',
  startTime: '',
  endDate: '',
  endTime: '',
  description: '',
  location: '',
  organizer: '',
});

function eventToForm(e: EventItem): FormState {
  const start = isoToDateAndTime(e.startDateTime);
  const end = isoToDateAndTime(e.endDateTime);
  return {
    name: e.name,
    startDate: start.date,
    startTime: start.time,
    endDate: end.date,
    endTime: end.time,
    description: e.description,
    location: e.location,
    organizer: e.organizer,
  };
}

type DateTimeRowProps = {
  idPrefix: string;
  dateLabel: string;
  timeLabel: string;
  dateValue: string;
  timeValue: string;
  onDateChange: (v: string) => void;
  onTimeChange: (v: string) => void;
  /** When true, the date field is required (e.g. event modal). */
  requireDate?: boolean;
};

function DateTimeRow({
  idPrefix,
  dateLabel,
  timeLabel,
  dateValue,
  timeValue,
  onDateChange,
  onTimeChange,
  requireDate,
}: DateTimeRowProps) {
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      <div>
        <label className={labelClass} htmlFor={`${idPrefix}-date`}>
          {dateLabel}
        </label>
        <input
          id={`${idPrefix}-date`}
          type="date"
          required={requireDate}
          value={dateValue}
          onChange={(e) => onDateChange(e.target.value)}
          className={`${inputClass} mt-1.5`}
        />
      </div>
      <div>
        <label className={labelClass} htmlFor={`${idPrefix}-time`}>
          {timeLabel}
        </label>
        <input
          id={`${idPrefix}-time`}
          type="time"
          value={timeValue}
          onChange={(e) => onTimeChange(e.target.value)}
          className={`${inputClass} mt-1.5`}
        />
      </div>
    </div>
  );
}

export function EventsPage() {
  const qc = useQueryClient();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState<number>(10);
  const [sortBy, setSortBy] = useState<EventsListParams['sortBy']>('name');
  const [sortDir, setSortDir] = useState<EventsListParams['sortDir']>('asc');

  const [filtersOpen, setFiltersOpen] = useState(false);
  const [searchInput, setSearchInput] = useState('');
  const debouncedSearch = useDebouncedValue(searchInput, SEARCH_DEBOUNCE_MS);
  const apiSearch =
    debouncedSearch.trim().length >= MIN_SEARCH_LEN ? debouncedSearch.trim() : '';

  const [rangeStartDate, setRangeStartDate] = useState('');
  const [rangeStartTime, setRangeStartTime] = useState('');
  const [rangeEndDate, setRangeEndDate] = useState('');
  const [rangeEndTime, setRangeEndTime] = useState('');

  const rangeStartIso = useMemo(
    () => (rangeStartDate ? rangeStartToIso(rangeStartDate, rangeStartTime) : ''),
    [rangeStartDate, rangeStartTime],
  );
  const rangeEndIso = useMemo(
    () => (rangeEndDate ? rangeEndToIso(rangeEndDate, rangeEndTime) : ''),
    [rangeEndDate, rangeEndTime],
  );

  const [panelOpen, setPanelOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form, setForm] = useState<FormState>(emptyForm());

  const listParams = useMemo<EventsListParams>(
    () => ({
      page,
      pageSize,
      sortBy,
      sortDir,
      search: apiSearch,
      rangeStart: rangeStartIso,
      rangeEnd: rangeEndIso,
    }),
    [page, pageSize, sortBy, sortDir, apiSearch, rangeStartIso, rangeEndIso],
  );

  const listUrl = useMemo(() => buildEventsListUrl(listParams), [listParams]);

  useEffect(() => {
    setPage(1);
  }, [apiSearch, rangeStartIso, rangeEndIso, sortBy, sortDir]);

  const locationsQuery = useQuery({
    queryKey: ['events', 'locations'],
    queryFn: () => apiJson<string[]>('/api/events/locations'),
  });

  const listQuery = useQuery({
    queryKey: ['events', 'list', listParams],
    queryFn: () => apiJson<PagedEvents>(listUrl),
  });

  const totalPages = useMemo(() => {
    const total = listQuery.data?.totalCount ?? 0;
    return Math.max(1, Math.ceil(total / pageSize));
  }, [listQuery.data?.totalCount, pageSize]);

  const invalidateEvents = useCallback(() => {
    void qc.invalidateQueries({ queryKey: ['events'] });
  }, [qc]);

  const createMutation = useMutation({
    mutationFn: (body: CreateEventBody) =>
      apiJson<EventItem>('/api/events', { method: 'POST', body: JSON.stringify(body) }),
    onSuccess: () => {
      setPanelOpen(false);
      setForm(emptyForm());
      setEditingId(null);
      invalidateEvents();
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateEventBody }) =>
      apiJson<EventItem>(`/api/events/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
    onSuccess: () => {
      setPanelOpen(false);
      setForm(emptyForm());
      setEditingId(null);
      invalidateEvents();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => apiJson<void>(`/api/events/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      invalidateEvents();
      if (listQuery.data && listQuery.data.items.length <= 1 && page > 1) {
        setPage((p) => Math.max(1, p - 1));
      }
    },
  });

  const busy =
    createMutation.isPending || updateMutation.isPending || deleteMutation.isPending;

  function openCreate() {
    setEditingId(null);
    const first = locationsQuery.data?.[0] ?? '';
    setForm({ ...emptyForm(), location: first });
    createMutation.reset();
    updateMutation.reset();
    setPanelOpen(true);
  }

  function openEdit(e: EventItem) {
    setEditingId(e.id);
    setForm(eventToForm(e));
    createMutation.reset();
    updateMutation.reset();
    setPanelOpen(true);
  }

  function closePanel() {
    setPanelOpen(false);
    setEditingId(null);
    setForm(emptyForm());
    createMutation.reset();
    updateMutation.reset();
  }

  function onSubmitForm(ev: FormEvent) {
    ev.preventDefault();
    const body: CreateEventBody = {
      name: form.name.trim(),
      startDateTime: eventLocalToIso(form.startDate, form.startTime),
      endDateTime: eventLocalToIso(form.endDate, form.endTime),
      description: form.description.trim(),
      location: form.location,
      organizer: form.organizer.trim(),
    };
    if (editingId) updateMutation.mutate({ id: editingId, body });
    else createMutation.mutate(body);
  }

  const locations = locationsQuery.data ?? [];
  const items = listQuery.data?.items ?? [];
  const formError = (createMutation.error ?? updateMutation.error) as Error | undefined;

  const hasActiveFilters =
    apiSearch.length >= MIN_SEARCH_LEN || Boolean(rangeStartDate) || Boolean(rangeEndDate);

  return (
    <div className="space-y-8">
      <header className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div className="space-y-2">
          <h2 className="text-2xl font-semibold text-white">Events</h2>
          <p className="max-w-2xl text-sm text-slate-400">
            Create and manage events. Open filters to search across name, location, and organizer, narrow by
            date and time range, and sort results. Range matches events that overlap the window.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            onClick={() => setFiltersOpen((o) => !o)}
            aria-expanded={filtersOpen}
            aria-controls="events-filters-panel"
            className={[
              'relative inline-flex min-h-11 items-center justify-center gap-2 rounded-xl border px-4 text-sm font-semibold transition-colors',
              filtersOpen || hasActiveFilters
                ? 'border-teal-400/50 bg-teal-500/15 text-teal-100'
                : 'border-white/15 bg-slate-900/60 text-slate-200 hover:bg-white/5',
            ].join(' ')}
          >
            <FilterIcon className="shrink-0 opacity-90" />
            Filters
            {hasActiveFilters ? (
              <span className="absolute -right-1 -top-1 flex h-2.5 w-2.5 rounded-full bg-teal-400 ring-2 ring-slate-950" />
            ) : null}
          </button>
          <button
            type="button"
            onClick={openCreate}
            disabled={busy || !locationsQuery.data?.length}
            className="inline-flex min-h-11 shrink-0 items-center justify-center rounded-xl bg-gradient-to-r from-teal-500 to-cyan-600 px-6 text-sm font-semibold text-white shadow-lg shadow-teal-500/20 hover:from-teal-400 hover:to-cyan-500 disabled:cursor-not-allowed disabled:opacity-50"
          >
            New event
          </button>
        </div>
      </header>

      {filtersOpen ? (
        <section
          id="events-filters-panel"
          className="rounded-2xl border border-white/10 bg-slate-900/40 p-6 shadow-inner shadow-black/20"
        >
          <h3 className="sr-only">Filters and sort</h3>
          <div className="space-y-6">
            <div>
              <label className={labelClass} htmlFor="f-search">
                Search
              </label>
              <input
                id="f-search"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                placeholder="Search"
                autoComplete="off"
                className={`${inputClass} mt-1.5`}
              />
              <p className="mt-2 text-xs text-slate-500">
                Matches name, location, or organizer. The list refreshes a moment after you stop typing; at least{' '}
                {MIN_SEARCH_LEN} characters are required to search.
              </p>
            </div>

            <div className="space-y-4">
              <p className={labelClass}>Range start</p>
              <DateTimeRow
                idPrefix="f-rs"
                dateLabel="Date"
                timeLabel="Time"
                dateValue={rangeStartDate}
                timeValue={rangeStartTime}
                onDateChange={setRangeStartDate}
                onTimeChange={setRangeStartTime}
              />
            </div>

            <div className="space-y-4">
              <p className={labelClass}>Range end</p>
              <DateTimeRow
                idPrefix="f-re"
                dateLabel="Date"
                timeLabel="Time"
                dateValue={rangeEndDate}
                timeValue={rangeEndTime}
                onDateChange={setRangeEndDate}
                onTimeChange={setRangeEndTime}
              />
            </div>

            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <label className={labelClass} htmlFor="f-sort">
                  Sort by
                </label>
                <select
                  id="f-sort"
                  value={sortBy}
                  onChange={(e) => setSortBy(e.target.value as EventsListParams['sortBy'])}
                  className={`${inputClass} mt-1.5`}
                >
                  <option value="name">Name</option>
                  <option value="start">Start</option>
                  <option value="end">End</option>
                </select>
              </div>
              <div>
                <label className={labelClass} htmlFor="f-dir">
                  Direction
                </label>
                <select
                  id="f-dir"
                  value={sortDir}
                  onChange={(e) => setSortDir(e.target.value as EventsListParams['sortDir'])}
                  className={`${inputClass} mt-1.5`}
                >
                  <option value="asc">Ascending</option>
                  <option value="desc">Descending</option>
                </select>
              </div>
            </div>
          </div>
        </section>
      ) : null}

      <section className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex flex-wrap items-center gap-3 text-sm text-slate-400">
          <span>Page size</span>
          <select
            value={pageSize}
            disabled={busy}
            onChange={(e) => {
              setPageSize(Number(e.target.value));
              setPage(1);
            }}
            className="rounded-lg border border-white/10 bg-slate-900 px-3 py-2 text-slate-100 focus:border-teal-400 focus:outline-none focus:ring-1 focus:ring-teal-400"
          >
            {pageSizeOptions.map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
          {listQuery.data ? (
            <span className="text-slate-500">
              Showing {(page - 1) * pageSize + 1}–{Math.min(page * pageSize, listQuery.data.totalCount)} of{' '}
              {listQuery.data.totalCount}
            </span>
          ) : null}
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            disabled={busy || page <= 1}
            onClick={() => setPage((p) => p - 1)}
            className="rounded-lg border border-white/10 px-4 py-2 text-sm font-medium text-slate-200 hover:bg-white/5 disabled:cursor-not-allowed disabled:opacity-40"
          >
            Previous
          </button>
          <span className="min-w-[7rem] text-center text-sm text-slate-400">
            Page {page} / {totalPages}
          </span>
          <button
            type="button"
            disabled={busy || page >= totalPages}
            onClick={() => setPage((p) => p + 1)}
            className="rounded-lg border border-white/10 px-4 py-2 text-sm font-medium text-slate-200 hover:bg-white/5 disabled:cursor-not-allowed disabled:opacity-40"
          >
            Next
          </button>
        </div>
      </section>

      {listQuery.isPending ? (
        <div className="space-y-3">
          {[0, 1, 2].map((i) => (
            <div key={i} className="h-28 animate-pulse rounded-xl bg-white/5" aria-hidden />
          ))}
          <p className="text-center text-sm text-slate-500">Loading events…</p>
        </div>
      ) : listQuery.error ? (
        <p className="rounded-xl border border-rose-500/30 bg-rose-950/40 px-4 py-3 text-rose-200">
          {(listQuery.error as Error).message}
        </p>
      ) : items.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-teal-500/20 bg-slate-900/30 px-6 py-16 text-center">
          <p className="text-lg font-medium text-slate-300">No events match</p>
          <p className="mt-2 text-sm text-slate-500">Adjust filters or add a new event.</p>
        </div>
      ) : (
        <ul className="grid gap-4 md:grid-cols-2">
          {items.map((ev) => (
            <li
              key={ev.id}
              className="group flex flex-col rounded-2xl border border-white/10 bg-gradient-to-br from-slate-900/95 to-slate-950/80 p-5 shadow-lg shadow-black/20 transition hover:border-teal-500/35"
            >
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <h4 className="truncate text-lg font-semibold text-white">{ev.name}</h4>
                  <p className="mt-1 text-xs font-mono text-slate-600">{ev.id}</p>
                </div>
                <span className="shrink-0 rounded-full border border-teal-500/30 bg-teal-500/10 px-2.5 py-1 text-xs font-medium text-teal-200">
                  {ev.location}
                </span>
              </div>
              <dl className="mt-4 space-y-2 text-sm text-slate-300">
                <div className="flex flex-col gap-0.5 sm:flex-row sm:justify-between">
                  <dt className="text-slate-500">Start</dt>
                  <dd className="font-medium text-slate-100">{formatRange(ev.startDateTime)}</dd>
                </div>
                <div className="flex flex-col gap-0.5 sm:flex-row sm:justify-between">
                  <dt className="text-slate-500">End</dt>
                  <dd className="font-medium text-slate-100">{formatRange(ev.endDateTime)}</dd>
                </div>
                <div className="flex flex-col gap-0.5 sm:flex-row sm:justify-between">
                  <dt className="text-slate-500">Organizer</dt>
                  <dd>{ev.organizer}</dd>
                </div>
              </dl>
              {ev.description ? (
                <p className="mt-3 line-clamp-3 text-sm leading-relaxed text-slate-400">{ev.description}</p>
              ) : null}
              <div className="mt-5 flex flex-wrap gap-2 border-t border-white/5 pt-4">
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => openEdit(ev)}
                  className="rounded-lg border border-teal-500/40 px-3 py-2 text-xs font-semibold text-teal-200 hover:bg-teal-500/10"
                >
                  Edit
                </button>
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => deleteMutation.mutate(ev.id)}
                  className="rounded-lg border border-rose-500/35 px-3 py-2 text-xs font-semibold text-rose-300 hover:bg-rose-500/10"
                >
                  Delete
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}

      {panelOpen ? (
        <div
          className="fixed inset-0 z-40 flex items-end justify-center bg-black/60 p-4 backdrop-blur-sm sm:items-center"
          role="dialog"
          aria-modal="true"
          aria-labelledby="event-panel-title"
        >
          <div className="max-h-[90vh] w-full max-w-lg overflow-y-auto rounded-2xl border border-white/10 bg-slate-950 p-6 shadow-2xl shadow-teal-950/40">
            <div className="flex items-start justify-between gap-4">
              <h3 id="event-panel-title" className="text-lg font-semibold text-white">
                {editingId ? 'Edit event' : 'New event'}
              </h3>
              <button
                type="button"
                onClick={closePanel}
                className="rounded-lg px-2 py-1 text-sm text-slate-400 hover:bg-white/10 hover:text-white"
              >
                Close
              </button>
            </div>

            <form onSubmit={onSubmitForm} className="mt-6 space-y-5">
              {formError ? (
                <pre className="whitespace-pre-wrap rounded-xl border border-rose-500/30 bg-rose-950/50 px-3 py-2 text-xs text-rose-100">
                  {formError.message}
                </pre>
              ) : null}

              <div>
                <label className={labelClass} htmlFor="pf-name">
                  Name (3–50 characters)
                </label>
                <input
                  id="pf-name"
                  required
                  minLength={3}
                  maxLength={50}
                  value={form.name}
                  onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))}
                  className={`${inputClass} mt-1.5`}
                />
              </div>

              <div>
                <label className={labelClass} htmlFor="pf-loc">
                  Location
                </label>
                <select
                  id="pf-loc"
                  required
                  value={form.location}
                  onChange={(e) => setForm((f) => ({ ...f, location: e.target.value }))}
                  className={`${inputClass} mt-1.5`}
                >
                  <option value="" disabled>
                    {locations.length ? 'Select venue' : 'Loading…'}
                  </option>
                  {locations.map((loc) => (
                    <option key={loc} value={loc}>
                      {loc}
                    </option>
                  ))}
                </select>
              </div>

              <div className="space-y-2">
                <p className={labelClass}>Start — date &amp; time</p>
                <DateTimeRow
                  idPrefix="pf-start"
                  dateLabel="Date"
                  timeLabel="Time"
                  dateValue={form.startDate}
                  timeValue={form.startTime}
                  onDateChange={(v) => setForm((f) => ({ ...f, startDate: v }))}
                  onTimeChange={(v) => setForm((f) => ({ ...f, startTime: v }))}
                  requireDate
                />
              </div>

              <div className="space-y-2">
                <p className={labelClass}>End — date &amp; time</p>
                <DateTimeRow
                  idPrefix="pf-end"
                  dateLabel="Date"
                  timeLabel="Time"
                  dateValue={form.endDate}
                  timeValue={form.endTime}
                  onDateChange={(v) => setForm((f) => ({ ...f, endDate: v }))}
                  onTimeChange={(v) => setForm((f) => ({ ...f, endTime: v }))}
                  requireDate
                />
              </div>

              <div>
                <label className={labelClass} htmlFor="pf-org">
                  Organizer (3–50 characters)
                </label>
                <input
                  id="pf-org"
                  required
                  minLength={3}
                  maxLength={50}
                  value={form.organizer}
                  onChange={(e) => setForm((f) => ({ ...f, organizer: e.target.value }))}
                  className={`${inputClass} mt-1.5`}
                />
              </div>

              <div>
                <label className={labelClass} htmlFor="pf-desc">
                  Description
                </label>
                <textarea
                  id="pf-desc"
                  rows={4}
                  maxLength={2000}
                  value={form.description}
                  onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))}
                  className={`${inputClass} mt-1.5 resize-y`}
                />
              </div>

              <div className="flex flex-wrap justify-end gap-2 pt-2">
                <button
                  type="button"
                  onClick={closePanel}
                  className="rounded-xl border border-white/15 px-5 py-2.5 text-sm font-medium text-slate-300 hover:bg-white/5"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={busy}
                  className="rounded-xl bg-teal-500 px-5 py-2.5 text-sm font-semibold text-white shadow-lg shadow-teal-500/25 hover:bg-teal-400 disabled:opacity-50"
                >
                  {busy ? 'Saving…' : editingId ? 'Save changes' : 'Create event'}
                </button>
              </div>
            </form>
          </div>
        </div>
      ) : null}
    </div>
  );
}
