export type EventItem = {
  id: string;
  name: string;
  startDateTime: string;
  endDateTime: string;
  description: string;
  location: string;
  organizer: string;
};

export type PagedEvents = {
  items: EventItem[];
  totalCount: number;
  page: number;
  pageSize: number;
};

export type CreateEventBody = {
  name: string;
  startDateTime: string;
  endDateTime: string;
  description: string;
  location: string;
  organizer: string;
};

export type UpdateEventBody = CreateEventBody;
