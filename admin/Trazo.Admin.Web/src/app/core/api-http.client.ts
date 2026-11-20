import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

/**
 * Shared transport for admin API domain services.
 * Authentication and token refresh remain in the application interceptor.
 * Domain services retain ownership of routes and typed API contracts.
 */
@Injectable({ providedIn: 'root' })
export class ApiHttpClient {
  private readonly client = inject(HttpClient);

  get<T>(url: string, options?: { params?: HttpParams }): Promise<T>;
  get(url: string, options: { params?: HttpParams; responseType: 'blob' }): Promise<Blob>;
  get<T>(url: string, options?: { params?: HttpParams; responseType?: 'blob' }): Promise<T | Blob> {
    if (options?.responseType === 'blob') {
      return firstValueFrom(this.client.get(url, { params: options.params, responseType: 'blob' }));
    }
    return firstValueFrom(this.client.get<T>(url, { params: options?.params }));
  }

  post<T>(url: string, body: unknown): Promise<T> {
    return firstValueFrom(this.client.post<T>(url, body));
  }

  put<T>(url: string, body: unknown, options?: { headers?: HttpHeaders | Record<string, string> }): Promise<T> {
    return firstValueFrom(this.client.put<T>(url, body, options));
  }

  patch<T>(url: string, body: unknown): Promise<T> {
    return firstValueFrom(this.client.patch<T>(url, body));
  }

  delete<T>(url: string, options?: { body?: unknown }): Promise<T> {
    return firstValueFrom(this.client.delete<T>(url, options));
  }
}
