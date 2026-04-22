import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router';
import { apiJson } from '../api/http';
import type { PagedEvents } from '../api/types';

export function HomePage() {
  const eventsSummaryQuery = useQuery({
    queryKey: ['events', 'summary'],
    queryFn: () => apiJson<PagedEvents>('/api/events?page=1&pageSize=1'),
  });

  const { isPending: loading, error, data } = eventsSummaryQuery;

  return (
    <div className="space-y-10">
      <section className="rounded-2xl border border-white/10 bg-gradient-to-br from-slate-900 to-slate-950 p-8 shadow-xl shadow-teal-950/30">
        <p className="text-sm font-medium text-teal-300">ASP.NET Core + React + Vite</p>
        <h2 className="mt-2 text-3xl font-semibold tracking-tight text-white">
          Full-stack starter template
        </h2>
        <p className="mt-3 max-w-2xl text-slate-400">
          Run <code className="rounded bg-white/10 px-1.5 py-0.5 text-slate-200">dotnet run</code> from{' '}
          <code className="rounded bg-white/10 px-1.5 py-0.5 text-slate-200">server/Api</code> (or F5).
          The API builds the React app into <code className="rounded bg-white/10 px-1.5 py-0.5 text-slate-200">wwwroot</code> and serves it with the API on one origin.
        </p>
        <div className="mt-6 flex flex-wrap gap-3">
          <Link
            to="/events"
            className="inline-flex items-center rounded-lg bg-gradient-to-r from-teal-500 to-cyan-600 px-4 py-2 text-sm font-semibold text-white shadow hover:from-teal-400 hover:to-cyan-500"
          >
            Open events
          </Link>
        </div>
      </section>

      <section className="grid gap-6 md:grid-cols-2">
        <div className="rounded-xl border border-white/10 bg-slate-900/50 p-6">
          <h3 className="text-lg font-semibold text-white">Live snapshot</h3>
          {loading ? (
            <p className="mt-4 text-slate-400">Loading…</p>
          ) : error ? (
            <p className="mt-4 text-rose-300">{(error as Error).message}</p>
          ) : (
            <dl className="mt-4 space-y-2 text-sm text-slate-300">
              <div className="flex justify-between gap-4">
                <dt>Events in catalog</dt>
                <dd className="font-mono text-teal-200">{data?.totalCount ?? 0}</dd>
              </div>
            </dl>
          )}
        </div>
        <div className="rounded-xl border border-white/10 bg-slate-900/50 p-6">
          <h3 className="text-lg font-semibold text-white">Stack</h3>
          <ul className="mt-4 list-inside list-disc space-y-1 text-sm text-slate-300">
            <li>.NET 10 Web API + events (FluentValidation)</li>
            <li>Swagger UI in Development</li>
            <li>React 19 + TypeScript + Vite</li>
            <li>TanStack Query + React Router 7 + Tailwind CSS 4</li>
          </ul>
        </div>
      </section>
    </div>
  );
}
