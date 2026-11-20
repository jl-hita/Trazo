import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import type {
  OperationsInput, OperationsView, PlayBillingInput, PlayBillingView, PromotionsSettings, PromotionsView,
  PushInput, PushSendResult, PushView
} from '../../core/admin.models';

const API = '/api/v1/admin/settings';

@Injectable({ providedIn: 'root' })
export class SettingsApi {
  private readonly http = inject(HttpClient);

  getPromotions(): Promise<PromotionsView> {
    return firstValueFrom(this.http.get<PromotionsView>(`${API}/promotions`));
  }

  savePromotions(settings: PromotionsSettings): Promise<PromotionsView> {
    return firstValueFrom(this.http.put<PromotionsView>(`${API}/promotions`, settings));
  }

  getAccessSettings(): Promise<{ subscriptionsEnforced: boolean }> {
    return firstValueFrom(this.http.get<{ subscriptionsEnforced: boolean }>(`${API}/access`));
  }

  saveAccessSettings(subscriptionsEnforced: boolean): Promise<{ subscriptionsEnforced: boolean }> {
    return firstValueFrom(this.http.put<{ subscriptionsEnforced: boolean }>(`${API}/access`, { subscriptionsEnforced }));
  }

  getPlayBilling(): Promise<PlayBillingView> {
    return firstValueFrom(this.http.get<PlayBillingView>(`${API}/play-billing`));
  }

  savePlayBilling(input: PlayBillingInput): Promise<{ saved: boolean }> {
    return firstValueFrom(this.http.put<{ saved: boolean }>(`${API}/play-billing`, input));
  }

  testPlayBilling(): Promise<{ ok: boolean; message: string }> {
    return firstValueFrom(this.http.post<{ ok: boolean; message: string }>(`${API}/play-billing/test`, {}));
  }

  getPush(): Promise<PushView> {
    return firstValueFrom(this.http.get<PushView>(`${API}/push`));
  }

  savePush(input: PushInput): Promise<{ saved: boolean }> {
    return firstValueFrom(this.http.put<{ saved: boolean }>(`${API}/push`, input));
  }

  checkPush(): Promise<PushSendResult> {
    return firstValueFrom(this.http.post<PushSendResult>(`${API}/push/check`, {}));
  }

  testPush(): Promise<PushSendResult> {
    return firstValueFrom(this.http.post<PushSendResult>(`${API}/push/test`, {}));
  }

  broadcastPush(title: string, body: string): Promise<PushSendResult> {
    return firstValueFrom(this.http.post<PushSendResult>(`${API}/push/broadcast`, { title, body }));
  }

  getOperations(): Promise<OperationsView> {
    return firstValueFrom(this.http.get<OperationsView>(`${API}/operations`));
  }

  saveOperations(input: OperationsInput): Promise<OperationsView> {
    return firstValueFrom(this.http.put<OperationsView>(`${API}/operations`, input));
  }

  testDietoExpress(endpoint: string): Promise<{ ok: boolean; message: string }> {
    return firstValueFrom(this.http.post<{ ok: boolean; message: string }>(`${API}/operations/test/dietoexpress`, { endpoint }));
  }
}
