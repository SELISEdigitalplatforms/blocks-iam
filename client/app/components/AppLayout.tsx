import { NavLink, Outlet } from 'react-router';

const linkClass = ({ isActive }: { isActive: boolean }) =>
  [
    'rounded-md px-3 py-2 text-sm font-medium transition-colors',
    isActive
      ? 'bg-indigo-500/20 text-indigo-200 ring-1 ring-indigo-400/40'
      : 'text-slate-300 hover:bg-white/5 hover:text-white',
  ].join(' ');

export function AppLayout() {
  return (
    <div className="min-h-screen">
      <header className="border-b border-white/10 bg-slate-900/80 backdrop-blur">
        <div className="mx-auto flex max-w-5xl flex-col gap-4 px-4 py-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex items-center gap-3">
            <div className="h-9 w-9 rounded-lg bg-gradient-to-br from-indigo-500 to-fuchsia-500 shadow-lg shadow-indigo-500/25" />
            <div>
              <p className="text-xs uppercase tracking-widest text-slate-400">Starter</p>
              <h1 className="text-lg font-semibold text-white">BlocksTemplate</h1>
            </div>
          </div>
          <nav className="flex flex-wrap gap-1">
            <NavLink to="/" end className={linkClass}>
              Home
            </NavLink>
            <NavLink to="/events" className={linkClass}>
              Events
            </NavLink>
            {import.meta.env.DEV ? (
              <a
                href="/swagger"
                className="rounded-md px-3 py-2 text-sm font-medium text-slate-300 hover:bg-white/5 hover:text-white"
              >
                Swagger
              </a>
            ) : null}
          </nav>
        </div>
      </header>
      <main className="mx-auto max-w-5xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  );
}
