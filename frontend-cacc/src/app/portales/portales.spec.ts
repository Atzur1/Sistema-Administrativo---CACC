import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Portales } from './portales';

describe('Portales', () => {
  let component: Portales;
  let fixture: ComponentFixture<Portales>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Portales],
    }).compileComponents();

    fixture = TestBed.createComponent(Portales);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
