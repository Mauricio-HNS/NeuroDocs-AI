import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';

type DocumentSummary = {
  id: string;
  fileName: string;
  sizeBytes: number;
  uploadedAt: string;
  totalPages: number;
  totalChunks: number;
  totalTokens: number;
  status: string;
};

type ChatResult = {
  answer: string;
  sources: { page: number; chunkIndex: number; preview: string }[];
};

type KnowledgeEntry = {
  id: string;
  type: string;
  title: string;
  content: string;
  sourceFileName?: string;
  createdAt: string;
};

type KnowledgeResult = {
  answer: string;
  sources: { id: string; type: string; title: string; preview: string; sourceFileName?: string }[];
};

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent {
  private api = 'http://localhost:5000/api';

  documents: DocumentSummary[] = [];
  selected?: DocumentSummary;
  question = '';
  teachTitle = '';
  teachContent = '';
  correction = '';
  knowledge: KnowledgeEntry[] = [];
  messages: { role: 'user' | 'assistant'; text: string; sources?: any[]; question?: string }[] = [];
  loading = false;

  constructor(private http: HttpClient) {
    this.loadDocuments();
    this.loadKnowledge();
  }

  loadDocuments() {
    this.http.get<DocumentSummary[]>(`${this.api}/documents`)
      .subscribe(x => this.documents = x);
  }

  loadKnowledge() {
    this.http.get<KnowledgeEntry[]>(`${this.api}/knowledge`)
      .subscribe(x => this.knowledge = x);
  }

  upload(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    const form = new FormData();
    form.append('file', file);

    this.loading = true;
    this.http.post<DocumentSummary>(`${this.api}/documents/upload`, form)
      .subscribe({
        next: doc => {
          this.documents = [doc, ...this.documents];
          this.selected = doc;
          this.loadKnowledge();
          this.loading = false;
        },
        error: () => this.loading = false
      });
  }

  select(doc: DocumentSummary) {
    this.selected = doc;
    this.messages = [];
  }

  ask() {
    if (!this.question.trim()) return;

    const question = this.question.trim();
    this.messages.push({ role: 'user', text: question, question });
    this.question = '';
    this.loading = true;

    const payload = {
      documentId: this.selected?.id,
      question
    };

    this.http.post<KnowledgeResult>(`${this.api}/knowledge/ask`, payload).subscribe({
      next: result => {
        this.messages.push({
          role: 'assistant',
          text: result.answer,
          question,
          sources: result.sources
        });
        this.loading = false;
      },
      error: () => {
        this.messages.push({
          role: 'assistant',
          text: 'Nao consegui processar essa pergunta agora.'
        });
        this.loading = false;
      }
    });
  }

  teach() {
    if (!this.teachContent.trim()) return;

    this.loading = true;
    this.http.post<KnowledgeEntry>(`${this.api}/knowledge/teach`, {
      title: this.teachTitle,
      content: this.teachContent,
      tags: ['manual']
    }).subscribe({
      next: () => {
        this.teachTitle = '';
        this.teachContent = '';
        this.loadKnowledge();
        this.loading = false;
      },
      error: () => this.loading = false
    });
  }

  saveCorrection(message: { question?: string }) {
    if (!message.question || !this.correction.trim()) return;

    this.loading = true;
    this.http.post<KnowledgeEntry>(`${this.api}/knowledge/feedback`, {
      question: message.question,
      correctAnswer: this.correction,
      documentId: this.selected?.id
    }).subscribe({
      next: () => {
        this.correction = '';
        this.loadKnowledge();
        this.loading = false;
      },
      error: () => this.loading = false
    });
  }
}
