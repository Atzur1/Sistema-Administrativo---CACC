# 🚛 Sistema de Gestión Integral - Club Atlético Camioneros (CACC)

## 📝 Descripción del Proyecto
El **Sistema de Gestión Integral CACC** es una plataforma web centralizada diseñada para optimizar y digitalizar la administración, las finanzas y la gestión deportiva del Club Atlético Camioneros. El sistema proporciona herramientas robustas para la toma de decisiones mediante paneles ejecutivos, control de accesos por roles y un seguimiento detallado de socios, personal y pagos.

---

## 🏗️ Arquitectura y Portales
El sistema está dividido lógicamente en diferentes entornos de trabajo para asegurar que cada usuario acceda solo a las herramientas relevantes para su rol:

* **Portal Administrativo:** Acceso exclusivo para personal administrativo, directivos y Superadmins. Orientado a la gestión de socios, control de cuotas, documentación y reportes financieros.
* **Portal Deportivo:** Acceso para profesores, entrenadores y coordinadores. Orientado a la gestión de jugadores, categorías, asistencia y seguimiento del rendimiento deportivo.
* **Portal de Socios (Comunidad):** Área dedicada a las consultas de los socios, visualización de actividades, cursos y noticias del gremio.

---

## ⚙️ Módulos Principales

### 1. Dashboards (Paneles de Control)
* **Dashboard Ejecutivo:** Vista de alto nivel con KPIs en tiempo real.
    * Métricas principales: Jugadores activos, Profesores en plantilla, Porcentaje de cuotas cobradas y Categorías activas.
    * Gráficos interactivos: Promedio de asistencia semanal.
* **Dashboard Administrativo:** Resumen de operaciones internas.
* **Dashboard Deportivo:** Resumen de actividades atléticas y rendimiento.

### 2. Administración y Finanzas
* **Usuarios y Permisos:** Control de acceso al sistema y gestión de altas de profesores y socios. Incluye métricas mensuales de inscripciones.
* **Cuotas y Pagos:** Módulo financiero integral para el seguimiento de la morosidad.
    * KPIs financieros: Total cobrado en el mes, Monto pendiente de cobro, Cantidad de cuotas vencidas y Becados activos.
    * Gráfico de evolución de cobros anual.
    * Tabla de gestión: Listado de socios con su categoría, estado de pago (Pagado, Vencido, Pendiente, Becado), método de pago (Transferencia, Efectivo, Débito) y opciones para registrar nuevos pagos.
* **Reportes y Estadísticas:** Funcionalidad para exportar datos del sistema para auditorías o análisis externos.

### 3. Gestión Deportiva y de Personal
* **Jugadores / Alumnos / Profesores:** ABM (Alta, Baja y Modificación) centralizado para todo el recurso humano del club.
    * Permite registrar Personas asignando: Nombre, Apellido, Rol (Jugador, Instructor, Médico, Personal), Área/Categoría, Documento, Teléfono y Estado (Activo, Inactivo, Pendiente, Licencia).
* **Categorías:** Organización de los socios en divisiones (ej. Infantil A, Pre-juvenil, Juvenil).

---

## 🔐 Seguridad y Acceso
* **Autenticación:** Sistema de login seguro con separación de roles (Usuario estándar vs. Administrador).
* **Trazabilidad:** Cada registro en el sistema (pagos, altas de personal) cuenta con fechas de registro y vinculación directa al perfil del usuario que ejecutó la acción.

---

## 🎨 Diseño y UI/UX
* **Identidad Visual:** Esquema de colores institucionales (verde y blanco) con el escudo oficial del CACC (dos estrellas).
* **Usabilidad:** Diseño limpio con navegación lateral (sidebar) colapsable, tablas de datos escaneables con *tags* de estado por colores (verde para activo/pagado, rojo para inactivo/vencido, amarillo para pendiente) y ventanas modales para la carga rápida de datos sin perder el contexto de la página.