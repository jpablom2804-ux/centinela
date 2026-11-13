package cr.centinela.mesaservicio.tickets.internal;

import java.util.UUID;
import org.springframework.data.jpa.repository.JpaRepository;

interface SugerenciaRepositorio extends JpaRepository<SugerenciaTriaje, UUID> {}
