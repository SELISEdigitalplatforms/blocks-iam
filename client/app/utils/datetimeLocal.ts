const timeRe = /^([01]?\d|2[0-3]):[0-5]\d$/;

function normalizedTime(time: string, fallback: string): string {
  return time && timeRe.test(time) ? time : fallback;
}

/** Parse ISO into separate date (yyyy-MM-dd) and time (HH:mm) in local timezone. */
export function isoToDateAndTime(iso: string): { date: string; time: string } {
  if (!iso) return { date: '', time: '' };
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return { date: '', time: '' };
  const pad = (x: number) => String(x).padStart(2, '0');
  return {
    date: `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`,
    time: `${pad(d.getHours())}:${pad(d.getMinutes())}`,
  };
}

/** Filter range start: default time 00:00 when omitted. */
export function rangeStartToIso(date: string, time: string): string {
  if (!date) return '';
  const t = normalizedTime(time, '00:00');
  return new Date(`${date}T${t}:00`).toISOString();
}

/** Filter range end: default time 23:59 when omitted (inclusive window). */
export function rangeEndToIso(date: string, time: string): string {
  if (!date) return '';
  const t = normalizedTime(time, '23:59');
  return new Date(`${date}T${t}:59.999`).toISOString();
}

/** Event form start/end: explicit date + time at second 0. */
export function eventLocalToIso(date: string, time: string): string {
  if (!date) return '';
  const t = normalizedTime(time, '00:00');
  return new Date(`${date}T${t}:00`).toISOString();
}
