/**
 * Object-URL window for reader pages.
 *
 * Only pages near the reading position get an object URL (and therefore a decoded image).
 * URLs inside `[current - keep, current + keep]` survive; anything outside is revoked.
 * With preload = 3 and keep = 6 at most 13 pages are alive at any moment, regardless of chapter length.
 */
export interface UrlFactory {
  create(blob: Blob): string;
  revoke(url: string): void;
}

export const browserUrls: UrlFactory = {
  create: (blob) => URL.createObjectURL(blob),
  revoke: (url) => URL.revokeObjectURL(url),
};

export class PageWindow {
  private readonly urls = new Map<number, string>();

  constructor(
    private readonly pageCount: number,
    private readonly pageBlob: (index: number) => Blob,
    private readonly factory: UrlFactory = browserUrls,
    readonly preload = 3,
    readonly keep = 6,
  ) {}

  /** Moves the window to `current` and returns the URLs that should be rendered. */
  update(current: number): Map<number, string> {
    const from = Math.max(0, current - this.preload);
    const to = Math.min(this.pageCount - 1, current + this.preload);
    for (let i = from; i <= to; i++) {
      if (!this.urls.has(i)) this.urls.set(i, this.factory.create(this.pageBlob(i)));
    }

    for (const [index, url] of this.urls) {
      if (index < current - this.keep || index > current + this.keep) {
        this.factory.revoke(url);
        this.urls.delete(index);
      }
    }

    return new Map(this.urls);
  }

  get liveCount() {
    return this.urls.size;
  }

  dispose() {
    for (const url of this.urls.values()) this.factory.revoke(url);
    this.urls.clear();
  }
}
