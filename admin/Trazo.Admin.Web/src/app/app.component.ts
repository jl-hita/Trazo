import { Component } from '@angular/core';

@Component({
  selector: 'trazo-root',
  standalone: true,
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  protected readonly navigation = [
    { label: 'Overview', icon: '◫', active: true },
    { label: 'Users', icon: '◎', active: false },
    { label: 'Subscriptions', icon: '◇', active: false },
    { label: 'Training library', icon: '▤', active: false },
    { label: 'Planning engine', icon: '⌘', active: false },
    { label: 'Logs', icon: '≋', active: false },
    { label: 'Backups', icon: '▣', active: false },
    { label: 'Settings', icon: '⚙', active: false }
  ];
}
